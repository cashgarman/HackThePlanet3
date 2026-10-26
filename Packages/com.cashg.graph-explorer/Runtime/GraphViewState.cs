using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph node view state.</summary>
	public sealed class GraphNodeViewState
	{
		/// <summary>Gets or sets the node's tool-specific emphasis identifier.</summary>
		public string Emphasis = "";
		/// <summary>Gets or sets whether layout keeps the node fixed.</summary>
		public bool Pinned;
		/// <summary>Gets or sets whether the node is visible.</summary>
		public bool Visible = true;
		/// <summary>Gets or sets whether the node matches active filters.</summary>
		public bool Matches = true;
		/// <summary>Gets or sets whether the node is marked as a favorite.</summary>
		public bool Favorite;
		/// <summary>Gets or sets the node's world position.</summary>
		public Vector3 Position;
		/// <summary>Gets or sets the node's layout velocity.</summary>
		public Vector3 Velocity;
	}

	/// <summary>Represents graph view state.</summary>
	public sealed class GraphViewState
	{
		/// <summary>Gets the identifiers of collapsed nodes.</summary>
		public readonly HashSet<string> FoldedNodeIds = new(StringComparer.Ordinal);
		private readonly Dictionary<string, GraphNodeViewState> nodes = new(StringComparer.Ordinal);
		/// <summary>Gets or sets the selected node identifier.</summary>
		public string SelectedId = "";
		/// <summary>Gets or sets the focused node identifier.</summary>
		public string FocusId = "";

		/// <summary>Creates view state scoped to a dataset.</summary>
		/// <param name="datasetId">Dataset identifier.</param>
		public GraphViewState(string datasetId)
		{
			DatasetId = GraphRecordCopy.Required(datasetId, nameof(datasetId));
			Nodes = new ReadOnlyDictionary<string, GraphNodeViewState>(nodes);
		}

		/// <summary>Gets mutable view state for nodes keyed by identifier.</summary>
		public IReadOnlyDictionary<string, GraphNodeViewState> Nodes { get; }
		/// <summary>Gets the dataset identifier that owns this state.</summary>
		public string DatasetId { get; }

		/// <summary>Gets existing view state or creates an initial state for a node.</summary>
		/// <param name="id">Node identifier.</param>
		/// <returns>The mutable view state for the node.</returns>
		public GraphNodeViewState GetOrCreate(string id)
		{
			GraphRecordCopy.Required(id, nameof(id));

			// Seed new view positions from the node ID so the initial layout is repeatable.
			if (!nodes.TryGetValue(id, out var state))
				nodes.Add(id, state = new GraphNodeViewState { Position = InitialPosition(id) });
			return state;
		}

		/// <summary>Removes state for nodes absent from a document and clears stale selection.</summary>
		/// <param name="document">Document whose nodes remain valid.</param>
		public void Synchronize(GraphDocument document)
		{
			if (document == null || document.DatasetId != DatasetId)
				throw new ArgumentException("View and document must belong to the same dataset.", nameof(document));

			// Remove state and selections that no longer refer to document nodes.
			foreach (var id in nodes.Keys.Where(id => !document.Nodes.ContainsKey(id)).ToArray()) nodes.Remove(id);
			FoldedNodeIds.RemoveWhere(id => !document.Nodes.ContainsKey(id));
			if (!document.Nodes.ContainsKey(SelectedId ?? "")) SelectedId = "";
			if (!document.Nodes.ContainsKey(FocusId ?? "")) FocusId = "";
		}

		/// <summary>Computes a stable initial position from a node identifier.</summary>
		/// <param name="id">Node identifier used to seed the position.</param>
		/// <returns>The deterministic initial world position.</returns>
		public static Vector3 InitialPosition(string id)
		{
			// Derive coordinates from SHA-256 bytes so independent views and process runs share the same seed layout.
			using (var hash = SHA256.Create())
			{
				var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(GraphRecordCopy.Required(id, nameof(id))));
				return new Vector3((BitConverter.ToUInt16(bytes, 0) / 65535f - .5f) * 65,
								   (BitConverter.ToUInt16(bytes, 2) / 65535f - .5f) * 40,
								   (BitConverter.ToUInt16(bytes, 4) / 65535f - .5f) * 45);
			}
		}
	}

	/// <summary>Represents graph explorer options.</summary>
	public sealed class GraphExplorerOptions
	{
		/// <summary>Creates renderer budget options.</summary>
		/// <param name="nodeRenderBudget">Maximum nodes considered for rendering.</param>
		/// <param name="nodeLabelBudget">Maximum node labels considered for rendering.</param>
		public GraphExplorerOptions(int nodeRenderBudget = 8000, int nodeLabelBudget = 48)
		{
			if (nodeRenderBudget < 1 || nodeLabelBudget < 1)
				throw new ArgumentOutOfRangeException(nameof(nodeRenderBudget));
			NodeRenderBudget = nodeRenderBudget;
			NodeLabelBudget = nodeLabelBudget;
		}

		/// <summary>Gets the node rendering budget.</summary>
		public int NodeRenderBudget { get; }
		/// <summary>Gets the node label rendering budget.</summary>
		public int NodeLabelBudget { get; }
	}
}
