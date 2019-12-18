using Portent;
using System;
using System.Collections.Generic;

namespace MinLA
{
    public class TopologicalOrderingInstance : IAnnealingProblem<int[]>
    {
        public int[] Result => _arrangementToDawgPointer;

        public double Cost { get; private set; }

        private readonly Random _rand = new Random(7654321);

        private bool BeforeChildren(int node, int newPosition)
        {
            var realFirstNode = _arrangementToDawgPointer[node];
            foreach (var neighbor in _convertedGraph[realFirstNode].Children)
            {
                var neighborPosition = _dawgToArrangementPointer[neighbor];
                if (neighborPosition <= newPosition)
                {
                    return false;
                }
            }

            return true;
        }

        public double MakeRandomMove()
        {
            //set _swapIndex1 and _swapIndex2
            //calculate and assign Delta
            var acceptablePosition = false;
            var count = 0;
            while (!acceptablePosition)
            {
                count++;
                if (count == 500)
                {
                    Console.WriteLine("Could not find a valid swap");
                    return double.MaxValue;
                }

                var t1 = _rand.Next(1, _arrangementToDawgPointer.Length);
                var t2 = _rand.Next(1, _arrangementToDawgPointer.Length);
                if (t1 == t2)
                {
                    continue;
                }

                _swapIndex1 = Math.Min(t1, t2);
                _swapIndex2 = Math.Max(t1, t2);
                acceptablePosition = BeforeChildren(_swapIndex1, _swapIndex2);
            }

            var realNode1 = _arrangementToDawgPointer[_swapIndex1];

            Delta = 0;
            foreach (var neighbor in _convertedGraph[realNode1].Neighbors)
            {
                var neighborArrangementPosition = _dawgToArrangementPointer[neighbor.Key];

                var oldCost = Math.Abs(_swapIndex1 - neighborArrangementPosition) * neighbor.Value;
                var newCost = Math.Abs(_swapIndex2 - neighborArrangementPosition) * neighbor.Value;
                Delta += newCost - oldCost;
            }

            var realNode2 = _arrangementToDawgPointer[_swapIndex2];
            foreach (var neighbor in _convertedGraph[realNode2].Neighbors)
            {
                var neighborArrangementPosition = _dawgToArrangementPointer[neighbor.Key];
                if (neighborArrangementPosition == _swapIndex1)
                {
                    continue;
                }

                var oldCost = Math.Abs(_swapIndex2 - neighborArrangementPosition) * neighbor.Value;
                var newCost = Math.Abs(_swapIndex1 - neighborArrangementPosition) * neighbor.Value;
                Delta += newCost - oldCost;
            }

            return Delta;
        }

        public void KeepLastMove()
        {
            Cost += Delta;

            var realIndexOfSwap1 = _arrangementToDawgPointer[_swapIndex1];
            var realIndexOfSwap2 = _arrangementToDawgPointer[_swapIndex2];

            _dawgToArrangementPointer[realIndexOfSwap1] = _swapIndex2;
            _dawgToArrangementPointer[realIndexOfSwap2] = _swapIndex1;
            _arrangementToDawgPointer[_swapIndex1] = realIndexOfSwap2;
            _arrangementToDawgPointer[_swapIndex2] = realIndexOfSwap1;
        }

        public double Delta { get; private set; }

        private int _swapIndex1;
        private int _swapIndex2;

        private readonly List<UndirectedGraphNode> _convertedGraph;

        private readonly CompressedSparseRowGraph _graph;
        public readonly int[] _arrangementToDawgPointer;
        private readonly int[] _dawgToArrangementPointer;

        public TopologicalOrderingInstance(CompressedSparseRowGraph graph)
        {
            _graph = graph;
            _convertedGraph = graph.Convert();
            _arrangementToDawgPointer = new int[_convertedGraph.Count];
            _dawgToArrangementPointer = new int[_convertedGraph.Count];
            for (var i = 0; i < _convertedGraph.Count; i++)
            {
                _arrangementToDawgPointer[i] = i;
                _dawgToArrangementPointer[i] = i;
            }

            CalculateFullCost();
        }

        private void CalculateFullCost()
        {
            Cost = _graph.ArrangementCost();
        }
    }
}
