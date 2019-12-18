using System;
using System.Collections.Generic;
using Portent;

namespace MinLA
{
    public class OrderingInstance : IAnnealingProblem<int[]>
    {
        public int[] Result => _arrangementToDawgPointer;

        public double Cost { get; private set; }

        private readonly Random _rand = new Random(7654321);

        public double MakeRandomMove()
        {
            //set _swapIndex1 and _swapIndex2
            //calculate and assign Delta

            _swapIndex1 = _rand.Next(_arrangementToDawgPointer.Length);
            _swapIndex2 = _rand.Next(_arrangementToDawgPointer.Length);

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

        public OrderingInstance(CompressedSparseRowGraph graph)
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
