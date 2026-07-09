This test verifies that ILRepack work when a class has duplicate field names.

The `Library.dll` contains a class with three fields named `A` but with different types.

The `TestMethod` change the values and make sure the class has the three fields after the merge and that the values are correct.

Check the [Library/README.md](Library/README.md) for the source code of the `Library.dll`.