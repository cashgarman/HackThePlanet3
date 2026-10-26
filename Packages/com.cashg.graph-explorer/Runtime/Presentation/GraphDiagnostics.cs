using Unity.Profiling;

namespace Cashg.GraphExplorer
{
	/// <summary>Stable marker names shared by live profiling and the opt-in baseline runner.</summary>
	public static class GraphDiagnostics
	{
		/// <summary>Profiler marker for graph update work.</summary>
		public static readonly ProfilerMarker Update = new("GraphExplorer.Update");
		/// <summary>Profiler marker for layout calculations.</summary>
		public static readonly ProfilerMarker Layout = new("GraphExplorer.Layout");
		/// <summary>Profiler marker for graph drawing.</summary>
		public static readonly ProfilerMarker Draw = new("GraphExplorer.Draw");
		/// <summary>Profiler marker for edge drawing.</summary>
		public static readonly ProfilerMarker Edges = new("GraphExplorer.Edges");
		/// <summary>Profiler marker for node picking.</summary>
		public static readonly ProfilerMarker PickNode = new("GraphExplorer.PickNode");
		/// <summary>Profiler marker for edge picking.</summary>
		public static readonly ProfilerMarker PickEdge = new("GraphExplorer.PickEdge");
		/// <summary>Profiler marker for node label layout and drawing.</summary>
		public static readonly ProfilerMarker NodeLabels = new("GraphExplorer.NodeLabels");
		/// <summary>Profiler marker for edge label layout and drawing.</summary>
		public static readonly ProfilerMarker EdgeLabels = new("GraphExplorer.EdgeLabels");
		/// <summary>Profiler marker for shell interface work.</summary>
		public static readonly ProfilerMarker Interface = new("GraphExplorer.Interface");
		/// <summary>Profiler marker for inspector rendering.</summary>
		public static readonly ProfilerMarker Inspector = new("GraphExplorer.Inspector");
		/// <summary>Profiler marker for queue rendering.</summary>
		public static readonly ProfilerMarker Queue = new("GraphExplorer.Queue");
	}
}
