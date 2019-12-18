using ChacoSharp;
using Portent;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using static ChacoSharp.Eigen.EigenSolve;
using static ChacoSharp.Graph.FindMaxDeg;
using static ChacoSharp.Graph.FreeGraph;
using static ChacoSharp.Graph.Reformat;
using static ChacoSharp.Input.CheckInput;
using static ChacoSharp.StaticConstants;
using static ChacoSharp.Utilities.MergeSort;
using static ChacoSharp.Utilities.Randomize;

namespace MinLA
{
    public static class ChacoSequencer
    {
        public static int[] SpectralSequence(CompressedSparseRowGraph graph)
        {
            Debug.WriteLine("Converting graph");
            var nodes = graph.Convert();
            var edgesPairs = nodes.SelectMany(x => x.Neighbors.OrderBy(y => y.Key)).ToArray();
            var adjacency = new int[nodes.Count + 1];
            var count = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                count += nodes[i].Neighbors.Count;
                adjacency[i + 1] = count;
            }

            var edges = new int[edgesPairs.Length + 1];
            var edgeWeights = new float[edgesPairs.Length + 1];
            Debug.WriteLine("edgePairs.Length: " + edgesPairs.Length);
            for (var i = 0; i < edgesPairs.Length; i++)
            {
                var pair = edgesPairs[i];
                edges[i] = pair.Key;
                edgeWeights[i] = Math.Max(pair.Value, 1.0f);
            }

            edges[^1] = -1;
            edgeWeights[^1] = -1.0f;

            Debug.WriteLine("  Sequence ready");
            Debug.WriteLine("    edges.Length:       " + edges.Length);
            Debug.WriteLine("    adjacency.Length:   " + adjacency.Length);
            Debug.WriteLine("    edgeWeights.Length: " + edgeWeights.Length);
            Debug.WriteLine("Done converting graph");
            return Calculate(edges, adjacency, edgeWeights);
        }

        private static unsafe int[] Calculate(int[] edges, int[] adj, float[] edgeWeights)
        {
            var edgeCopy = new int[edges.Length];
            for (var i = 0; i < edges.Length; i++)
            {
                edgeCopy[i] = edges[i] + 1;
            }

            //if (graph.e)
            fixed (int* edgeP = adj)
            {
                fixed (int* edgeCopyPointer = edgeCopy)
                {
                    fixed (float* edgeWeightPointer = edgeWeights)
                    {
                        return SequenceVertices(adj.Length - 1,
                            edgeP,
                            edgeCopyPointer,
                            edgeWeightPointer,
                            EIGEN_TOLERANCE,
                            7654321,
                            LanczosType.SelectiveOrthogonalization
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Returns the spectral ordering of the supplied graph.
        /// </summary>
        /// <param name="vertexCount">number of vertices in full graph</param>
        /// <param name="start">start of edge list for each vertex</param>
        /// <param name="adjacency">edge list data</param>
        /// <param name="edgeWeights">weights for all edges</param>
        /// <param name="eigtol">tolerance on eigenvectors</param>
        /// <param name="seed">for random graph mutations</param>
        /// <param name="lanczosType">which eigensolver to use</param>
        /// <returns>A flag indicating if an error occured.</returns>
        private static unsafe int[] SequenceVertices(int vertexCount,
            int* start,
            int* adjacency,
            float* edgeWeights,
            double eigtol,
            int seed,
            LanczosType lanczosType
        )
        {
            if (vertexCount < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount), nameof(vertexCount) + "should be > 1");
            }

            if (RQI_CONVERGENCE_MODE != 0)
            {
                throw new InvalidOperationException(nameof(RQI_CONVERGENCE_MODE) + " should be 0 when using " + nameof(SequenceVertices) + " = true");
            }

            if (LANCZOS_CONVERGENCE_MODE != 0)
            {
                throw new InvalidOperationException(nameof(LANCZOS_CONVERGENCE_MODE) + " should be 0 when using " + nameof(SequenceVertices) + " = true");
            }

            vtx_data** graph = null;
            try
            {
                var edgeCount = 0; /* number of edges in graph */

                Debug.WriteLine("Reformatting graph for Chaco");
                // transform the edges and vertices into the required format.
                reformat(start, adjacency, vertexCount, &edgeCount, null, edgeWeights, &graph);
                Debug.WriteLine("Done reformatting graph for Chaco");

                // Perform some one-time initializations.
                setrandom(seed);

                // Turn off perturbation.
                PERTURB = false;

                if (CHECK_INPUT)
                {
                    Debug.WriteLine("Checking Chaco input");
                    var vmax = int.MaxValue;
                    // Check the input for inconsistencies.
                    var flag = check_input(graph, vertexCount, edgeCount, 0, null, null, new[] {vertexCount / 2.0d, vertexCount / 2.0d}, 0, 1, null, StaticConstants.PartitioningStrategy.Spectral, StaticConstants.LocalPartitioningStrategy.None, false, &vmax, 1, eigtol);
                    if (flag)
                    {
                        Debug.WriteLine("ERROR IN INPUT.");
                        throw new ArgumentOutOfRangeException(nameof(start), "ChacoSharp rejected the input graph. This may be due to configuration options or an error in the graph. Enable more logging for details.");
                    }

                    Debug.WriteLine("Done checking Chaco input");
                }

                var useEdgeWeights = edgeWeights != null;
                var maxWeightedVertexDegree = find_maxdeg(graph, vertexCount, useEdgeWeights, null);
                var firstVector = new double[vertexCount + 1];
                var scratchSpaceArray = new int[vertexCount]; // Memory for the called methods to use as scratch space

                fixed (double* firstVectorPtr = firstVector)
                {
                    fixed (int* scratchSpace = scratchSpaceArray)
                    {
                        // Space for pointing to eigenvectors
                        var eigenVectors = new double*[MAXDIMS + 1];
                        eigenVectors[1] = firstVectorPtr;

                        Debug.WriteLine("Begin Chaco.eigensolve");
                        eigensolve(graph,
                            vertexCount,
                            edgeCount,
                            maxWeightedVertexDegree,
                            1, // Largest vertex weight
                            null, // Square roots of vertex weights
                            false, // Are vertex weights being used
                            useEdgeWeights,
                            new float*[] {null, null}, // Dummy vector for terminal weights
                            0, // Geometric dimensionality if there were vertex coords
                            null, // Vertex coordinates
                            eigenVectors,
                            new double[MAXDIMS + 1], // EigenValues
                            false, // false for hypercube architecture
                            scratchSpace,
                            new[] {vertexCount / 2.0d, vertexCount / 2.0d}, // Goal needed for eigen convergence mode = 1
                            lanczosType,
                            false, // Use multi-level solver
                            int.MaxValue, // Subgraph size to stop coarsening in multi-level mode
                            1, // number of eigenvectors
                            MappingType.IndependantMedians, // Partitioning strategy
                            eigtol // Tolerance on eigenvectors
                        );
                        Debug.WriteLine("Done Chaco.eigensolve");

                        if (eigenVectors[1] == null)
                        {
                            Marshal.FreeHGlobal((IntPtr) scratchSpace);
                            throw new InvalidOperationException("Eigenvector was not returned fom call to " + nameof(eigensolve));
                        }

                        var result = new int[vertexCount];
                        fixed (int* resultPtr = &result[0])
                        {
                            ch_mergesort(&eigenVectors[1][1], vertexCount, resultPtr, scratchSpace);
                        }

                        return result;
                    }
                }
            }
            finally
            {
                free_graph(graph);
            }
        }
    }
}
