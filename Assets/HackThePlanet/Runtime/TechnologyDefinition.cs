using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Scriptable research node on the technology tree with costs, grants, and rules.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Technology")]
	public sealed class TechnologyDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets the research branch name.</summary>
		public string branch;

		/// <summary>Gets or sets the credit cost to unlock.</summary>
		public int cost;

		/// <summary>Gets or sets the waypoint tier on the tree (0–4).</summary>
		[Range(0, 4)] public int waypointTier;

		/// <summary>Gets or sets whether this node is a special milestone.</summary>
		public bool special;

		/// <summary>Gets or sets the layout position on the technology map.</summary>
		public Vector2 position;

		/// <summary>Gets or sets parent technologies that must be unlocked first.</summary>
		public TechnologyDefinition[] parents = Array.Empty<TechnologyDefinition>();

		/// <summary>Gets or sets actions unlocked when this technology is researched.</summary>
		public ActionDefinition[] grants = Array.Empty<ActionDefinition>();

		/// <summary>Gets or sets passive stat modifiers granted by ownership.</summary>
		public Modifier[] modifiers = Array.Empty<Modifier>();

		/// <summary>Gets or sets event-driven triggers granted by ownership.</summary>
		public Trigger[] triggers = Array.Empty<Trigger>();
	}
}