# portent

[![Build status](https://ci.appveyor.com/api/projects/status/8g8n9bd3wh3boddb?svg=true)](https://ci.appveyor.com/project/jeanbern/portent)
<!--[![codecov](https://codecov.io/gh/jeanbern/portent/branch/master/graph/badge.svg)](https://codecov.io/gh/jeanbern/portent)-->

A highly optimized C# implementation of fuzzy word matching with a Directed Acyclic Word Graph.  

For background, visit my blog: <https://jbp.dev/blog/dawg-basics.html>  
Eventually there will be a host of content explaining the design decisions involved in creating the Dawg class.
1. Performance enhancements in `Lookup()`
2. Memory management in the `Dawg()` constructor
3. Node ordering in `PartitionedGraphBuilder.TopologicalSort()`
4. Reduction of allocations with custom `SuggestItemCollections`