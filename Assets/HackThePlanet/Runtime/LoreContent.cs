using System;
using System.Linq;

namespace HackThePlanet
{
	/// <summary>Story fragment tied to companies, contracts, and narrative text.</summary>
	[Serializable]
	public sealed class LoreFragment
	{
		/// <summary>Gets or sets the fragment identifier.</summary>
		public string id, title, company, contract, text;
	}

	/// <summary>Helpers for resolving lore fragments on a <see cref="Contract" />.</summary>
	public static class LoreContent
	{
		/// <summary>Gets the lore fragment at the given index, or null if out of range or lore is missing.</summary>
		/// <param name="job">Contract whose lore array is queried.</param>
		/// <param name="index">Zero-based index into <paramref name="job" />.lore.</param>
		/// <returns>The fragment at <paramref name="index" />, or null.</returns>
		public static LoreFragment For(Contract job, int index)
		{
			// Return the fragment only when lore exists and the index is in range.
			return job.lore != null && index >= 0 && index < job.lore.Length ? job.lore[index] : null;
		}

		/// <summary>Finds the first lore fragment on the contract with the given id.</summary>
		/// <param name="job">Contract to search; may be null.</param>
		/// <param name="id">Fragment id to match; null or empty returns null.</param>
		/// <returns>The matching fragment, or null if not found.</returns>
		public static LoreFragment Find(Contract job, string id)
		{
			// Return null for an empty id, otherwise search the contract's lore by id.
			return string.IsNullOrEmpty(id) ? null : job?.lore?.FirstOrDefault(x => x.id == id);
		}
	}
}