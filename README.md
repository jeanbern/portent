# Portent

[![Build status](https://ci.appveyor.com/api/projects/status/8g8n9bd3wh3boddb?svg=true)](https://ci.appveyor.com/project/jeanbern/portent)
<!--[![codecov](https://codecov.io/gh/jeanbern/portent/branch/master/graph/badge.svg)](https://codecov.io/gh/jeanbern/portent)-->
[![Codacy Badge](https://api.codacy.com/project/badge/Grade/6dc3f766018a42f8a08b29e3afda2bc2)](https://www.codacy.com/manual/jeanbern/portent?utm_source=github.com&amp;utm_medium=referral&amp;utm_content=jeanbern/portent&amp;utm_campaign=Badge_Grade)

[![Total alerts](https://img.shields.io/lgtm/alerts/g/jeanbern/portent.svg?logo=lgtm&logoWidth=18)](https://lgtm.com/projects/g/jeanbern/portent/alerts/)
<!--[![Language grade: C#](https://img.shields.io/lgtm/grade/csharp/g/jeanbern/portent.svg?logo=lgtm&logoWidth=18)](https://lgtm.com/projects/g/jeanbern/portent/context:csharp)-->

A highly optimized C# implementation of fuzzy word matching with a Directed Acyclic Word Graph.  

For background, visit my blog: <https://jbp.dev/blog/dawg-basics.html>  
Eventually there will be a host of content explaining the design decisions involved in creating the Dawg class.
1. Performance enhancements in `Lookup()`
2. Memory management in the `Dawg()` constructor
3. Node ordering in `PartitionedGraphBuilder.TopologicalSort()`
4. Reduction of allocations with custom `SuggestItemCollections`
