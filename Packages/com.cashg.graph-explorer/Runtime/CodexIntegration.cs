using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Cashg.GraphExplorer.Codex
{
	/// <summary>Lists codex launch mode.</summary>
	public enum CodexLaunchMode
	{
		/// <summary>Opens a new chat prefilled with a workspace and prompt.</summary>
		PrefilledChat,

		/// <summary>Starts an automatically dispatched task.</summary>
		AutomaticTask
	}

	/// <summary>Represents codex links.</summary>
	public static class CodexLinks
	{
		/// <summary>Creates a link that opens a new chat for an existing workspace.</summary>
		/// <param name="workspace">Absolute path to an existing workspace directory.</param>
		/// <param name="prompt">Prompt to prefill in the chat.</param>
		/// <returns>A Codex deep link.</returns>
		public static string NewChat(string workspace, string prompt)
		{
			if (!Path.IsPathRooted(workspace) || !Directory.Exists(workspace))
				throw new ArgumentException("An existing absolute workspace is required.");
			return "codex://new?path=" + Uri.EscapeDataString(Path.GetFullPath(workspace)) + "&prompt=" +
				   Uri.EscapeDataString(prompt ?? "");
		}

		/// <summary>Creates a link that opens an existing task.</summary>
		/// <param name="id">Task identifier.</param>
		/// <returns>A Codex deep link.</returns>
		public static string Thread(string id)
		{
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A thread ID is required.");
			return "codex://threads/" + Uri.EscapeDataString(id);
		}
	}

	/// <summary>Injectable line transport. No Unity objects are touched by IO threads.</summary>
	public interface ICodexTransport : IDisposable
	{
		/// <summary>Occurs when a complete protocol line is received.</summary>
		event Action<string> Line;

		/// <summary>Occurs when the transport closes, with a reason.</summary>
		event Action<string> Closed;

		/// <summary>Starts the transport.</summary>
		void Start();

		/// <summary>Sends one protocol line.</summary>
		/// <param name="line">Line to send.</param>
		void Send(string line);
	}

	/// <summary>Represents codex process transport.</summary>
	public sealed class CodexProcessTransport : ICodexTransport
	{
		private readonly string executable;
		private bool disposed;
		private Process process;

		/// <summary>Creates a standard input and output transport for the Codex app server.</summary>
		/// <param name="executable">Path or command name for the Codex executable.</param>
		public CodexProcessTransport(string executable)
		{
			this.executable = executable;
		}

		/// <summary>Occurs when the process writes a protocol line.</summary>
		public event Action<string> Line;

		/// <summary>Occurs when the process output closes.</summary>
		public event Action<string> Closed;

		/// <summary>Starts the app server process and begins reading its output.</summary>
		public void Start()
		{
			// Launch an isolated stdio process and deliver complete output lines through transport events.
			var info = new ProcessStartInfo
			{
				FileName = executable, Arguments = "app-server --stdio", UseShellExecute = false,
				RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
				CreateNoWindow = true
			};
			process = new Process { StartInfo = info, EnableRaisingEvents = true };
			process.OutputDataReceived += (_, e) =>
			{
				if (e.Data != null) Line?.Invoke(e.Data);
				else if (!disposed) Closed?.Invoke("Codex output closed.");
			};
			// Always drain stderr so a full pipe cannot stall the server. Protocol errors arrive on stdout.
			process.ErrorDataReceived += (_, e) => { };
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}

		/// <summary>Sends a line to the app server's standard input.</summary>
		/// <param name="line">Protocol line to send.</param>
		public void Send(string line)
		{
			process.StandardInput.WriteLine(line);
			process.StandardInput.Flush();
		}

		/// <summary>Stops the process and releases its resources.</summary>
		public void Dispose()
		{
			disposed = true;
			if (process == null) return;

			// The process may exit between the state check and Kill; either way, release its handles afterward.
			try
			{
				if (!process.HasExited) process.Kill();
			}
			catch (InvalidOperationException)
			{
			}

			process.Dispose();
			process = null;
		}
	}

	/// <summary>JSON-RPC client pumped on the host thread. Requests never retry implicitly.</summary>
	public sealed class CodexAppServerClient : IDisposable
	{
		private readonly ConcurrentQueue<string> closures = new();
		private readonly ConcurrentQueue<string> lines = new();
		private readonly Dictionary<long, Pending> pending = new();
		private readonly ICodexTransport transport;
		private bool disposed, closed;
		private long nextId;

		/// <summary>Creates a JSON-RPC client over a line transport.</summary>
		/// <param name="transport">Transport used for protocol input and output.</param>
		public CodexAppServerClient(ICodexTransport transport)
		{
			this.transport = transport;
			transport.Line += OnLine;
			transport.Closed += OnClosed;
		}

		/// <summary>Gets whether the initialization handshake completed.</summary>
		public bool Ready { get; private set; }

		/// <summary>Closes the transport and discards pending requests.</summary>
		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			Ready = false;
			transport.Line -= OnLine;
			transport.Closed -= OnClosed;
			transport.Dispose();
			pending.Clear();
		}

		/// <summary>Occurs when the server sends a notification or request.</summary>
		public event Action<string, JsonElement, JsonElement?> Message;

		/// <summary>Occurs when the transport disconnects.</summary>
		public event Action<string> Disconnected;

		private void OnLine(string line)
		{
			lines.Enqueue(line);
		}

		private void OnClosed(string reason)
		{
			closures.Enqueue(reason);
		}

		/// <summary>Starts the transport and initializes the app-server session.</summary>
		/// <param name="ready">Called when initialization succeeds.</param>
		/// <param name="failed">Called when startup or initialization fails.</param>
		public void Start(Action ready, Action<string> failed)
		{
			// Complete the initialize/initialized handshake before exposing the client as ready.
			try
			{
				transport.Start();
				Request("initialize", new
				{
					clientInfo = new { name = "graph_explorer", title = "Graph Explorer", version = "1.0.0" },
					capabilities = new { experimentalApi = false }
				}, _ =>
				{
					Notify("initialized", new { });
					Ready = true;
					ready();
				}, failed);
			}
			catch (Exception e)
			{
				failed(e.Message);
			}
		}

		/// <summary>Sends a JSON-RPC request and routes its response to a callback.</summary>
		/// <param name="method">RPC method name.</param>
		/// <param name="parameters">Method parameters serialized as JSON.</param>
		/// <param name="done">Receives a successful result.</param>
		/// <param name="failed">Receives a transport, protocol, or timeout error.</param>
		public void Request(string method, object parameters, Action<JsonElement> done, Action<string> failed)
		{
			if (disposed || closed)
			{
				failed("Codex connection is closed.");
				return;
			}

			var id = ++nextId;

			// Register the response callbacks before sending so synchronous transport replies can be matched.
			pending[id] = new Pending { Done = done, Failed = failed, Deadline = DateTime.UtcNow.AddMinutes(2) };
			try
			{
				transport.Send(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));
			}
			catch (Exception e)
			{
				pending.Remove(id);
				failed(e.Message);
			}
		}

		/// <summary>Sends a JSON-RPC notification without creating a pending request.</summary>
		/// <param name="method">RPC method name.</param>
		/// <param name="parameters">Method parameters serialized as JSON.</param>
		public void Notify(string method, object parameters)
		{
			transport.Send(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }));
		}

		/// <summary>Sends a successful response to an incoming JSON-RPC request.</summary>
		/// <param name="id">Request identifier from the incoming message.</param>
		/// <param name="result">Result serialized as JSON.</param>
		public void Respond(JsonElement id, object result)
		{
			transport.Send(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
		}

		/// <summary>Sends a method-not-found error response to an incoming request.</summary>
		/// <param name="id">Request identifier from the incoming message.</param>
		/// <param name="reason">Error message to return.</param>
		public void Reject(JsonElement id, string reason)
		{
			transport.Send(JsonSerializer.Serialize(new
			{
				jsonrpc = "2.0", id,
				error = new { code = -32601, message = reason }
			}));
		}

		/// <summary>Processes queued protocol lines, timeouts, and transport closures.</summary>
		public void Pump()
		{
			if (disposed) return;
			// Bound each frame's work; remaining messages stay queued.
			for (var i = 0; i < 200 && lines.TryDequeue(out var line); i++)
				try
				{
					using var doc = JsonDocument.Parse(line);
					var root = doc.RootElement;
					var hasId = root.TryGetProperty("id", out var id);
					if (root.TryGetProperty("method", out var method))
					{
						// Clone JSON values that must outlive the document disposed at the end of this iteration.
						var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : default;
						Message?.Invoke(method.GetString(), parameters, hasId ? id.Clone() : null);
					}
					else if (hasId && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) &&
							 pending.TryGetValue(number, out var request))
					{
						pending.Remove(number);
						if (root.TryGetProperty("error", out var error)) request.Failed(error.ToString());
						else if (root.TryGetProperty("result", out var result)) request.Done(result.Clone());
						else request.Failed("Codex response has no result.");
					}
				}
				catch (JsonException)
				{
					closures.Enqueue("Invalid JSON from Codex app-server.");
				}

			// Expire requests without retrying operations whose external outcome may already have changed.
			foreach (var pair in pending.Where(p => p.Value.Deadline < DateTime.UtcNow).ToArray())
			{
				pending.Remove(pair.Key);
				pair.Value.Failed("Codex response timed out; reconcile before retrying.");
			}

			if (closures.TryDequeue(out var reason) && !closed)
			{
				// Fail all outstanding callbacks once and publish the disconnected state on the host thread.
				closed = true;
				Ready = false;
				var requests = pending.Values.ToArray();
				pending.Clear();
				foreach (var request in requests) request.Failed(reason);
				Disconnected?.Invoke(reason);
			}
		}

		private sealed class Pending
		{
			public DateTime Deadline;
			public Action<JsonElement> Done;
			public Action<string> Failed;
		}
	}
}