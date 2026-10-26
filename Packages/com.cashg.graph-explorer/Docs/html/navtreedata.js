/*
 @licstart  The following is the entire license notice for the JavaScript code in this file.

 The MIT License (MIT)

 Copyright (C) 1997-2020 by Dimitri van Heesch

 Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 and associated documentation files (the "Software"), to deal in the Software without restriction,
 including without limitation the rights to use, copy, modify, merge, publish, distribute,
 sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

 The above copyright notice and this permission notice shall be included in all copies or
 substantial portions of the Software.

 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
 BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

 @licend  The above is the entire license notice for the JavaScript code in this file
*/
var NAVTREE =
[
  [ "GraphExplorer", "index.html", [
    [ "Changelog", "md__c_h_a_n_g_e_l_o_g.html", [
      [ "0.1.0 — 2026-10-22", "md__c_h_a_n_g_e_l_o_g.html#autotoc_md1", [
        [ "CodebaseGraph integration — 2026-10-23", "md__c_h_a_n_g_e_l_o_g.html#autotoc_md2", null ],
        [ "Checkpoint 3 — rendering boundary (in progress)", "md__c_h_a_n_g_e_l_o_g.html#autotoc_md3", null ]
      ] ]
    ] ],
    [ "Graph Explorer — Architecture &amp; API Reference", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html", [
      [ "Table of contents", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md5", null ],
      [ "Architecture overview", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md6", null ],
      [ "Assemblies &amp; dependencies", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md7", null ],
      [ "Core API (<span class=\"tt\">Cashg.GraphExplorer</span>)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md9", [
        [ "<span class=\"tt\">Runtime/GraphClusterScene.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md10", [
          [ "<span class=\"tt\">GraphClusterScene</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md11", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphDocument.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md12", [
          [ "<span class=\"tt\">GraphIdSet</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md13", null ],
          [ "<span class=\"tt\">GraphDocument</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md14", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphJson.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md15", [
          [ "<span class=\"tt\">GraphJson</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md16", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphPathfinder.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md17", [
          [ "<span class=\"tt\">GraphPathEndpoint</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md18", null ],
          [ "<span class=\"tt\">GraphPathfinder</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md19", null ],
          [ "<span class=\"tt\">GraphPathResult</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md20", null ],
          [ "<span class=\"tt\">GraphPathState</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md21", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphPresentationContracts.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md22", [
          [ "<span class=\"tt\">LayoutMode</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md23", null ],
          [ "<span class=\"tt\">SizeMapping</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md24", null ],
          [ "<span class=\"tt\">ColorMapping</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md25", null ],
          [ "<span class=\"tt\">ToolEmphasis</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md26", null ],
          [ "<span class=\"tt\">IGraphNode</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md27", null ],
          [ "<span class=\"tt\">IGraphEdge</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md28", null ],
          [ "<span class=\"tt\">IGraphScene</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md29", null ],
          [ "<span class=\"tt\">IGraphEdgeFilter</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md30", null ],
          [ "<span class=\"tt\">GraphPresentationProfile</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md31", null ],
          [ "<span class=\"tt\">GraphNodeProgressVisual</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md32", null ],
          [ "<span class=\"tt\">GraphTaskVisual</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md33", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphProviders.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md34", [
          [ "<span class=\"tt\">GraphProviderProgress</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md35", null ],
          [ "<span class=\"tt\">IGraphDataProvider</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md36", null ],
          [ "<span class=\"tt\">GraphActionParameter</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md37", null ],
          [ "<span class=\"tt\">GraphActionDefinition</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md38", null ],
          [ "<span class=\"tt\">GraphActionRequest</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md39", null ],
          [ "<span class=\"tt\">GraphActionEntryProgress</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md40", null ],
          [ "<span class=\"tt\">GraphActionRun</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md41", null ],
          [ "<span class=\"tt\">GraphProviderException</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md42", null ],
          [ "<span class=\"tt\">IGraphActionProvider</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md43", null ],
          [ "<span class=\"tt\">IGraphActionHistoryProvider</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md44", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphQueueCoordinator.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md45", [
          [ "<span class=\"tt\">GraphQueueCoordinator</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md46", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphRecords.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md47", [
          [ "<span class=\"tt\">GraphValueType</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md48", null ],
          [ "<span class=\"tt\">GraphValue</span> (struct)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md49", null ],
          [ "<span class=\"tt\">GraphNodeRecord</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md50", null ],
          [ "<span class=\"tt\">GraphEdgeRecord</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md51", null ],
          [ "<span class=\"tt\">GraphSnapshot</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md52", null ],
          [ "<span class=\"tt\">GraphBatch</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md53", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphRevisionTimeline.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md54", [
          [ "<span class=\"tt\">GraphRevision</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md55", null ],
          [ "<span class=\"tt\">GraphRevisionLaneLink</span> (struct)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md56", null ],
          [ "<span class=\"tt\">GraphRevisionChangeSummary</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md57", null ],
          [ "<span class=\"tt\">GraphRevisionTimeline</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md58", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphScene.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md59", [
          [ "<span class=\"tt\">GraphDictionaryView</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md60", null ],
          [ "<span class=\"tt\">GraphScene</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md61", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphSchema.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md62", [
          [ "<span class=\"tt\">GraphCategoryDefinition</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md63", null ],
          [ "<span class=\"tt\">GraphFieldDefinition</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md64", null ],
          [ "<span class=\"tt\">GraphNodeTypeDefinition</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md65", null ],
          [ "<span class=\"tt\">GraphRelationDefinition</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md66", null ],
          [ "<span class=\"tt\">GraphSchema</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md67", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphStorage.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md68", [
          [ "<span class=\"tt\">GraphStorage</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md69", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphTimeline.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md70", [
          [ "<span class=\"tt\">GraphPositionFrame</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md71", null ],
          [ "<span class=\"tt\">GraphTimelineChange</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md72", null ],
          [ "<span class=\"tt\">GraphTimelineEntry</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md73", null ],
          [ "<span class=\"tt\">GraphTimeline</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md74", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphTimelineEventTrack.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md75", [
          [ "<span class=\"tt\">GraphTimelineEvent</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md76", null ],
          [ "<span class=\"tt\">GraphTimelineEventTrack</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md77", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphTimelineWorkspace.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md78", [
          [ "<span class=\"tt\">IGraphTimelineEventProvider</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md79", null ],
          [ "<span class=\"tt\">GraphCachedTimelineEvent</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md80", null ],
          [ "<span class=\"tt\">GraphTimelineWorkspace</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md81", null ],
          [ "<span class=\"tt\">GraphSnapshotCache</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md82", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphViewState.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md83", [
          [ "<span class=\"tt\">GraphNodeViewState</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md84", null ],
          [ "<span class=\"tt\">GraphViewState</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md85", null ],
          [ "<span class=\"tt\">GraphExplorerOptions</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md86", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/GraphWorkflow.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md87", [
          [ "<span class=\"tt\">GraphWorkflowState</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md88", null ],
          [ "<span class=\"tt\">GraphWorkflowGrouping</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md89", null ],
          [ "<span class=\"tt\">GraphWorkflowStep</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md90", null ],
          [ "<span class=\"tt\">GraphWorkflowValidation</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md91", null ],
          [ "<span class=\"tt\">GraphWorkflowTarget</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md92", null ],
          [ "<span class=\"tt\">GraphWorkflowTask</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md93", null ],
          [ "<span class=\"tt\">IGraphWorkflowAcceptance</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md94", null ],
          [ "<span class=\"tt\">IGraphExternalTaskProvider</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md95", null ],
          [ "<span class=\"tt\">GraphWorkflow</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md96", null ]
        ] ]
      ] ],
      [ "Presentation API (<span class=\"tt\">Cashg.GraphExplorer.Presentation</span>)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md97", [
        [ "<span class=\"tt\">Runtime/Presentation/ForceLayout.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md98", [
          [ "<span class=\"tt\">ForceLayout</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md99", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphAtmosphere.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md100", [
          [ "<span class=\"tt\">GraphAtmosphere</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md101", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphDiagnostics.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md102", [
          [ "<span class=\"tt\">GraphDiagnostics</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md103", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphHeaderIcons.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md104", [
          [ "<span class=\"tt\">HeaderIcons</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md105", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphNavigation.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md106", [
          [ "<span class=\"tt\">GraphNavigation</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md107", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphNodeEffects.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md108", [
          [ "<span class=\"tt\">GraphNodeEffects</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md109", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphNodeLabelSettings.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md110", [
          [ "<span class=\"tt\">GraphNodeLabelSettings</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md111", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphNodeModels.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md112", [
          [ "<span class=\"tt\">GraphNodeModels</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md113", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphPathPresenter.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md114", [
          [ "<span class=\"tt\">GraphPathRendering</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md115", null ],
          [ "<span class=\"tt\">GraphPathPresenter</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md116", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphQueueView.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md117", [
          [ "<span class=\"tt\">GraphQueueView</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md118", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphRenderer.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md119", [
          [ "<span class=\"tt\">GraphRenderer</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md120", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphRenderingSession.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md121", [
          [ "<span class=\"tt\">GraphRenderingSession</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md122", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphResources.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md123", [
          [ "<span class=\"tt\">GraphResources</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md124", null ],
          [ "<span class=\"tt\">GraphResourceConfiguration</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md125", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphSceneInput.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md126", [
          [ "<span class=\"tt\">GraphSceneInput</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md127", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphTimelineControls.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md128", [
          [ "<span class=\"tt\">GraphTimelineControls</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md129", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphTimelineViewport.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md130", [
          [ "<span class=\"tt\">GraphTimelineViewport</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md131", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphUi.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md132", [
          [ "<span class=\"tt\">GraphUi</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md133", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphVisualSettings.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md134", [
          [ "<span class=\"tt\">GraphVisualSettings</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md135", null ]
        ] ],
        [ "<span class=\"tt\">Runtime/Presentation/GraphWorkflowView.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md136", [
          [ "<span class=\"tt\">GraphWorkflowView</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md137", null ]
        ] ]
      ] ],
      [ "Codex (<span class=\"tt\">Cashg.GraphExplorer.Codex</span>)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md138", [
        [ "<span class=\"tt\">Runtime/CodexIntegration.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md139", [
          [ "<span class=\"tt\">CodexLaunchMode</span> (enum)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md140", null ],
          [ "<span class=\"tt\">CodexLinks</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md141", null ],
          [ "<span class=\"tt\">ICodexTransport</span> (interface)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md142", null ],
          [ "<span class=\"tt\">CodexProcessTransport</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md143", null ],
          [ "<span class=\"tt\">CodexAppServerClient</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md144", null ]
        ] ]
      ] ],
      [ "Editor (<span class=\"tt\">Cashg.GraphExplorer.Editor</span>)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md145", [
        [ "<span class=\"tt\">Editor/GraphDocumentValidation.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md146", [
          [ "<span class=\"tt\">GraphDocumentValidation</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md147", null ]
        ] ],
        [ "<span class=\"tt\">Editor/GraphNodeModelImporter.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md148", [
          [ "<span class=\"tt\">GraphNodeModelImporter</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md149", null ]
        ] ],
        [ "<span class=\"tt\">Editor/GraphResourceValidation.cs</span>", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md150", [
          [ "<span class=\"tt\">GraphResourceValidation</span> (class)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md151", null ]
        ] ]
      ] ],
      [ "Usage examples", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md152", [
        [ "Bootstrap document and scene", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md153", null ],
        [ "Force layout + render loop", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md154", null ],
        [ "GraphJson round-trip", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md155", null ],
        [ "Provider load (Unity coroutine)", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md156", null ],
        [ "Cluster + LOD", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md157", null ],
        [ "Shortest path highlight", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md158", null ],
        [ "Action queue", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md159", null ],
        [ "Scoped storage", "md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md160", null ]
      ] ]
    ] ],
    [ "Graph Explorer SDK", "md__documentation_0i_2_s_d_k.html", [
      [ "1. Scope and feature status", "md__documentation_0i_2_s_d_k.html#autotoc_md163", null ],
      [ "2. Install and assembly boundaries", "md__documentation_0i_2_s_d_k.html#autotoc_md164", null ],
      [ "3. Model terminology and rules", "md__documentation_0i_2_s_d_k.html#autotoc_md165", [
        [ "Reversible neighborhood clustering", "md__documentation_0i_2_s_d_k.html#autotoc_md166", null ],
        [ "Shortest paths", "md__documentation_0i_2_s_d_k.html#autotoc_md167", null ],
        [ "Imported revision graphs", "md__documentation_0i_2_s_d_k.html#autotoc_md168", null ],
        [ "Create and update an ordinary dataset", "md__documentation_0i_2_s_d_k.html#autotoc_md169", null ]
      ] ],
      [ "4. Schema, appearance and layout", "md__documentation_0i_2_s_d_k.html#autotoc_md170", null ],
      [ "5. Rendering, navigation and lifetime", "md__documentation_0i_2_s_d_k.html#autotoc_md171", [
        [ "Ownership and resources", "md__documentation_0i_2_s_d_k.html#autotoc_md172", null ]
      ] ],
      [ "6. Compose the UI", "md__documentation_0i_2_s_d_k.html#autotoc_md173", null ],
      [ "7. Dataset providers", "md__documentation_0i_2_s_d_k.html#autotoc_md174", null ],
      [ "8. Action providers and reviewed queues", "md__documentation_0i_2_s_d_k.html#autotoc_md175", null ],
      [ "9. Workspaces, favorites and storage", "md__documentation_0i_2_s_d_k.html#autotoc_md176", null ],
      [ "10. Source map, validation and AI handoff", "md__documentation_0i_2_s_d_k.html#autotoc_md177", null ],
      [ "External history events and custom drawers", "md__documentation_0i_2_s_d_k.html#autotoc_md178", null ],
      [ "Timeline ranges, portable caches, and saved zoom", "md__documentation_0i_2_s_d_k.html#autotoc_md179", null ],
      [ "Host-owned node progress and workflows", "md__documentation_0i_2_s_d_k.html#autotoc_md180", [
        [ "Optional Codex integration", "md__documentation_0i_2_s_d_k.html#autotoc_md181", null ]
      ] ]
    ] ],
    [ "Graph Explorer", "md__r_e_a_d_m_e.html", null ],
    [ "Packages", "namespaces.html", [
      [ "Package List", "namespaces.html", "namespaces_dup" ],
      [ "Package Members", "namespacemembers.html", [
        [ "All", "namespacemembers.html", null ],
        [ "Functions", "namespacemembers_func.html", null ],
        [ "Enumerations", "namespacemembers_enum.html", null ]
      ] ]
    ] ],
    [ "Classes", "annotated.html", [
      [ "Class List", "annotated.html", "annotated_dup" ],
      [ "Class Index", "classes.html", null ],
      [ "Class Hierarchy", "hierarchy.html", "hierarchy" ],
      [ "Class Members", "functions.html", [
        [ "All", "functions.html", "functions_dup" ],
        [ "Functions", "functions_func.html", "functions_func" ],
        [ "Variables", "functions_vars.html", null ],
        [ "Properties", "functions_prop.html", "functions_prop" ],
        [ "Events", "functions_evnt.html", null ]
      ] ]
    ] ],
    [ "Files", "files.html", [
      [ "File List", "files.html", "files_dup" ]
    ] ]
  ] ]
];

var NAVTREEINDEX =
[
"_codex_integration_8cs_source.html",
"class_cashg_1_1_graph_explorer_1_1_graph_cluster_scene.html#a775a46f580ef64fb2afb6b742a292643",
"class_cashg_1_1_graph_explorer_1_1_graph_inspector_options.html#a70b9ff9adcade4caa4f5aab9b4a4ed53",
"class_cashg_1_1_graph_explorer_1_1_graph_provider_exception.html#a7b962c1bd6e00e7249b27294da182b5b",
"class_cashg_1_1_graph_explorer_1_1_graph_schema.html#aa59f4d8220d477e6f621bbbe80d806dd",
"class_cashg_1_1_graph_explorer_1_1_graph_timeline_controls.html#ae548585b9807b3b43a2d7ccffccedeea",
"class_cashg_1_1_graph_explorer_1_1_graph_workflow_task.html#aa58108aa1954b269cbf8d858f2ff6805",
"class_cashg_1_1_graph_explorer_1_1_tests_1_1_graph_presentation_tests.html#adf7631e412a3654012adf3f33d3d00e5",
"interface_cashg_1_1_graph_explorer_1_1_i_graph_position_history_shell_host.html#a4aa46f595a9d9d743af6e81f0e4d2276",
"md__documentation_0i_2_a_r_c_h_i_t_e_c_t_u_r_e.html#autotoc_md144"
];

const SYNCONMSG = 'click to disable panel synchronization';
const SYNCOFFMSG = 'click to enable panel synchronization';
const LISTOFALLMEMBERS = 'List of all members';