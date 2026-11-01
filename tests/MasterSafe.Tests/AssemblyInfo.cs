// File:    AssemblyInfo.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Xunit;

// The language is one setting for the whole program, and some tests change it, so tests do not run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
