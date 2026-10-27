using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Scriptable network node type with visuals, security, tags, and auras.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Node Type")]
	public sealed class NodeTypeDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets the node kind used for graph model lookup.</summary>
		public NodeKind glyph;

		/// <summary>Gets or sets the map color (default cyan).</summary>
		public Color color = Color.cyan;

		/// <summary>Gets or sets gameplay tags associated with this node type.</summary>
		public string[] tags = Array.Empty<string>();

		/// <summary>Gets or sets the evaluated security level (default constant 1).</summary>
		[SerializeReference] public Number security = new ConstantNumber(1);

		/// <summary>Gets or sets passive auras emitted by nodes of this type.</summary>
		public Aura[] auras = Array.Empty<Aura>();
	}
}