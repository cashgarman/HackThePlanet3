using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>Captured gameplay catalog and golden network hashes used for one-time asset import.</summary>
	[Serializable]
	public sealed class GameplaySnapshot
	{
		/// <summary>Gets or sets company display names from the baseline capture.</summary>
		public string[] companies;

		/// <summary>Gets or sets story and recurring contracts from the baseline capture.</summary>
		public Contract[] contracts;

		/// <summary>Gets or sets research technology rows from the baseline capture.</summary>
		public Technology[] technologies;

		/// <summary>Gets or sets representative node prototypes per <see cref="NodeKind" />.</summary>
		public NetNode[] prototypes;

		/// <summary>Gets or sets lore fragments bundled with the baseline.</summary>
		public List<LoreFragment> lore = new();

		/// <summary>Gets or sets expected network topology hashes for regression checks.</summary>
		public List<NetworkGolden> networks = new();
	}

	/// <summary>Expected SHA-256 fingerprint for one generated network scenario.</summary>
	[Serializable]
	public sealed class NetworkGolden
	{
		/// <summary>Gets or sets the generation seed under test.</summary>
		public int seed;

		/// <summary>Gets or sets the contract difficulty tier used for generation.</summary>
		public int difficulty;

		/// <summary>Gets or sets whether career hardening modifiers were applied.</summary>
		public bool hardened;

		/// <summary>Gets or sets the expected <see cref="GameplayBaseline.Hash" /> value.</summary>
		public string hash;
	}

	/// <summary>JSON-serializable subset of a generated network used for deterministic hashing.</summary>
	[Serializable]
	public sealed class NetworkSnapshot
	{
		/// <summary>Gets or sets normalized node records included in the hash.</summary>
		public GoldenNode[] nodes;

		/// <summary>Gets or sets network links included in the hash.</summary>
		public List<NetLink> links;

		/// <summary>Gets or sets approach routes excluded from JSON serialization.</summary>
		[NonSerialized] public List<List<string>> approaches;

		/// <summary>Copies topology fields from a live generated network into a hashable snapshot.</summary>
		/// <param name="n">Generated network to capture.</param>
		public NetworkSnapshot(GeneratedNetwork n)
		{
			// Copy each node's topology fields into a hashable record.
			nodes = n.nodes.Select(x => new GoldenNode
			{
				id = x.id, label = x.label, detail = x.detail, kind = x.kind, security = x.security, column = x.column,
				row = x.row, x = x.x, y = x.y, suppressed = x.suppressed, known = x.known, controlled = x.controlled,
				scanned = x.scanned, collected = x.collected, objective = x.objective, bonus = x.bonus,
				position = x.position, loreId = x.loreId
			}).ToArray();

			// Copy the links and approach routes that define the network shape.
			links = n.links;
			approaches = n.approaches;
		}
	}

	/// <summary>Node fields compared when hashing or golden-testing generated networks.</summary>
	[Serializable]
	public sealed class GoldenNode
	{
		/// <summary>Gets or sets the local node identifier.</summary>
		public string id;

		/// <summary>Gets or sets the player-facing node label.</summary>
		public string label;

		/// <summary>Gets or sets detailed intel copy for the node.</summary>
		public string detail;

		/// <summary>Gets or sets the node role kind.</summary>
		public NodeKind kind;

		/// <summary>Gets or sets initial security level.</summary>
		public int security;

		/// <summary>Gets or sets legacy logical column metadata.</summary>
		public int column;

		/// <summary>Gets or sets legacy logical row metadata.</summary>
		public int row;

		/// <summary>Gets or sets legacy 2D X metadata.</summary>
		public float x;

		/// <summary>Gets or sets legacy 2D Y metadata.</summary>
		public float y;

		/// <summary>Gets or sets defense suppression amount on the node.</summary>
		public float suppressed;

		/// <summary>Gets or sets whether the node starts discovered.</summary>
		public bool known;

		/// <summary>Gets or sets whether the operator controls the node at start.</summary>
		public bool controlled;

		/// <summary>Gets or sets whether the node was scanned.</summary>
		public bool scanned;

		/// <summary>Gets or sets whether payload data was collected.</summary>
		public bool collected;

		/// <summary>Gets or sets whether the node is a required objective.</summary>
		public bool objective;

		/// <summary>Gets or sets whether the node is an optional archive.</summary>
		public bool bonus;

		/// <summary>Gets or sets graph placement in 3D space.</summary>
		public Vector3 position;

		/// <summary>Gets or sets the lore fragment id assigned to optional archives.</summary>
		public string loreId;
	}

	/// <summary>Helpers for fingerprinting generated network topology during baseline tests.</summary>
	public static class GameplayBaseline
	{
		/// <summary>Hashes the JSON encoding of a <see cref="NetworkSnapshot" /> for golden comparisons.</summary>
		/// <param name="n">Network whose topology should be fingerprinted.</param>
		/// <returns>Uppercase hex SHA-256 digest without separators.</returns>
		public static string Hash(GeneratedNetwork n)
		{
			// Create a SHA-256 hasher for the snapshot bytes.
			using (var sha = SHA256.Create())
			{
				// Serialize the snapshot, hash it, and format the digest as hex without dashes.
				return BitConverter
					   .ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new NetworkSnapshot(n)))))
					   .Replace("-", "");
			}
		}
	}
}