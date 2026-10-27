using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cashg.GraphExplorer
{
	/// <summary>Namespaced, read-only defaults. Loading assets never creates a scene or connects a provider.</summary>
	public static class GraphResources
	{
		/// <summary>Gets the Resources folder prefix used by the package.</summary>
		public const string Root = "CashgGraphExplorer/";

		/// <summary>Loads a package asset by key.</summary>
		/// <typeparam name="T">Asset type to load.</typeparam>
		/// <param name="key">Key relative to <see cref="Root" />.</param>
		/// <returns>The loaded asset, or null when it is unavailable.</returns>
		public static T Load<T>(string key) where T : Object
		{
			return Resources.Load<T>(Root + key);
		}
	}

	/// <summary>Explicit per-explorer overrides. Assets are borrowed; only runtime clones are owned.</summary>
	public sealed class GraphResourceConfiguration
	{
		private readonly Dictionary<string, Object> overrides = new(StringComparer.Ordinal);

		/// <summary>Registers an asset override for an explorer instance.</summary>
		/// <typeparam name="T">Asset type.</typeparam>
		/// <param name="key">Resource key.</param>
		/// <param name="asset">Asset to use for the key.</param>
		public void Set<T>(string key, T asset) where T : Object
		{
			if (string.IsNullOrWhiteSpace(key) || asset == null)
				throw new ArgumentException("A resource key and asset are required.");
			overrides[key] = asset;
		}

		/// <summary>Loads an override when present, otherwise loads the package default.</summary>
		/// <typeparam name="T">Expected asset type.</typeparam>
		/// <param name="key">Resource key.</param>
		/// <returns>The configured or default asset, or null when no asset is available.</returns>
		public T Load<T>(string key) where T : Object
		{
			return overrides.TryGetValue(key, out var asset)
				? asset as T ?? throw new InvalidOperationException("Resource has the wrong type: " + key)
				: GraphResources.Load<T>(key);
		}
	}
}