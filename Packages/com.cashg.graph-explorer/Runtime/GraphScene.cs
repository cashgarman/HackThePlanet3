using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>Covariant dictionary view. Identity and ownership stay with the source.</summary>
	public sealed class GraphDictionaryView<TSource, T> : IReadOnlyDictionary<string, T> where TSource : T
	{
		private readonly IReadOnlyDictionary<string, TSource> source;

		/// <summary>Creates a read-only view that exposes source values through a base type.</summary>
		/// <param name="source">Dictionary to wrap.</param>
		public GraphDictionaryView(IReadOnlyDictionary<string, TSource> source)
		{
			this.source = source;
		}

		/// <summary>Gets the value associated with the specified identifier.</summary>
		/// <param name="id">Key to look up.</param>
		/// <returns>The value associated with the key.</returns>
		public T this[string id] => source[id];

		/// <summary>Gets the keys in the source dictionary.</summary>
		public IEnumerable<string> Keys => source.Keys;

		/// <summary>Gets the source values as the view's value type.</summary>
		public IEnumerable<T> Values
		{
			get
			{
				foreach (var value in source.Values) yield return value;
			}
		}

		/// <summary>Gets the number of entries in the source dictionary.</summary>
		public int Count => source.Count;

		/// <summary>Determines whether the source contains the specified key.</summary>
		/// <param name="id">Key to find.</param>
		/// <returns><see langword="true" /> when the key exists.</returns>
		public bool ContainsKey(string id)
		{
			return source.ContainsKey(id);
		}

		/// <summary>Attempts to get the value associated with a key.</summary>
		/// <param name="id">Key to look up.</param>
		/// <param name="value">Receives the value when the key exists.</param>
		/// <returns><see langword="true" /> when the key exists.</returns>
		public bool TryGetValue(string id, out T value)
		{
			if (source.TryGetValue(id, out var item))
			{
				value = item;
				return true;
			}

			value = default;
			return false;
		}

		/// <summary>Enumerates the key and value pairs in the view.</summary>
		/// <returns>An enumerator over the key and value pairs.</returns>
		public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
		{
			foreach (var item in source) yield return new KeyValuePair<string, T>(item.Key, item.Value);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	/// <summary>Default document/view projection for hosts that do not need domain facades.</summary>
	public sealed class GraphScene : IGraphScene
	{
		private readonly Dictionary<string, IGraphEdge> edges = new(StringComparer.Ordinal);
		private readonly Dictionary<string, IGraphNode> nodes = new(StringComparer.Ordinal);

		/// <summary>Creates a scene that combines document data with view state and schema.</summary>
		/// <param name="document">Document supplying nodes and edges.</param>
		/// <param name="schema">Schema describing graph types and fields.</param>
		/// <param name="view">Optional view state; a state for the document is created when omitted.</param>
		public GraphScene(GraphDocument document, GraphSchema schema, GraphViewState view = null)
		{
			Document = document ?? throw new ArgumentNullException(nameof(document));
			Schema = schema ?? throw new ArgumentNullException(nameof(schema));
			View = view ?? new GraphViewState(document.DatasetId);
			if (View.DatasetId != document.DatasetId)
				throw new ArgumentException("View belongs to another dataset.", nameof(view));
			Nodes = new ReadOnlyDictionary<string, IGraphNode>(nodes);
			Edges = new ReadOnlyDictionary<string, IGraphEdge>(edges);
			Synchronize();
		}

		/// <summary>Gets the backing graph document.</summary>
		public GraphDocument Document { get; }

		/// <summary>Gets the scene's mutable view state.</summary>
		public GraphViewState View { get; }

		/// <summary>Gets the schema used to interpret graph records.</summary>
		public GraphSchema Schema { get; }

		/// <summary>Gets the scene's nodes keyed by identifier.</summary>
		public IReadOnlyDictionary<string, IGraphNode> Nodes { get; }

		/// <summary>Gets the scene's edges keyed by identifier.</summary>
		public IReadOnlyDictionary<string, IGraphEdge> Edges { get; }

		/// <summary>Gets the document topology revision represented by the scene.</summary>
		public int Revision => Document.TopologyRevision;

		/// <summary>Synchronizes scene node and edge views with the document.</summary>
		public void Synchronize()
		{
			// Prune removed facts while retaining wrappers and view state for surviving identities.
			View.Synchronize(Document);
			foreach (var id in nodes.Keys.Where(id => !Document.Nodes.ContainsKey(id)).ToArray()) nodes.Remove(id);
			foreach (var id in edges.Keys.Where(id => !Document.Edges.ContainsKey(id)).ToArray()) edges.Remove(id);

			// Add wrappers only for new facts; existing wrappers read updated records directly from the document.
			foreach (var id in Document.Nodes.Keys)
				if (!nodes.ContainsKey(id))
					nodes.Add(id, new Node(this, id));
			foreach (var id in Document.Edges.Keys)
				if (!edges.ContainsKey(id))
					edges.Add(id, new Edge(this, id));
		}

		private sealed class Node : IGraphNode
		{
			private readonly GraphScene owner;
			private readonly GraphNodeViewState view;

			public Node(GraphScene owner, string id)
			{
				this.owner = owner;
				Id = id;
				view = owner.View.GetOrCreate(id);
				Edges = new Incident(owner, id);
			}

			public string Id { get; }
			public GraphNodeRecord Record => owner.Document.Nodes[Id];
			public string Label => Record.Label;

			public Vector3 Position
			{
				get => view.Position;
				set => view.Position = value;
			}

			public Vector3 Velocity
			{
				get => view.Velocity;
				set => view.Velocity = value;
			}

			public bool Pinned
			{
				get => view.Pinned;
				set => view.Pinned = value;
			}

			public bool Visible
			{
				get => view.Visible;
				set => view.Visible = value;
			}

			public bool Matches
			{
				get => view.Matches;
				set => view.Matches = value;
			}

			public bool Favorite
			{
				get => view.Favorite;
				set => view.Favorite = value;
			}

			public ToolEmphasis Emphasis
			{
				get => Enum.TryParse(view.Emphasis, true, out ToolEmphasis emphasis) ? emphasis : ToolEmphasis.None;
				set => view.Emphasis = value.ToString().ToLowerInvariant();
			}

			public GraphIdSet Neighbors => owner.Document.Neighbors(Id);
			public IReadOnlyList<IGraphEdge> Edges { get; }
		}

		private sealed class Edge : IGraphEdge
		{
			private readonly GraphScene owner;

			public Edge(GraphScene owner, string id)
			{
				this.owner = owner;
				Id = id;
			}

			public string Id { get; }
			public GraphEdgeRecord Record => owner.Document.Edges[Id];
			public string From => Record.From;
			public string To => Record.To;
			public string Relation => Record.TypeId;
			public string Status => Record.Status;
		}

		private sealed class Incident : IReadOnlyList<IGraphEdge>
		{
			private readonly string id;
			private readonly GraphScene owner;

			public Incident(GraphScene owner, string id)
			{
				this.owner = owner;
				this.id = id;
			}

			public int Count => owner.Document.IncidentEdges(id).Count;
			public IGraphEdge this[int index] => owner.edges[owner.Document.IncidentEdges(id).ElementAt(index)];

			public IEnumerator<IGraphEdge> GetEnumerator()
			{
				foreach (var key in owner.Document.IncidentEdges(id)) yield return owner.edges[key];
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}
	}
}