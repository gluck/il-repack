This is a 'fork' or 'clone' or the original github Cecil repository.
Not meant at being neither disrespectful nor long lived, but will allow code sharing for the time being.

Patches from original are:
- removed "context ?? " in Import.cs to correctly import Generics with a context
- allows transient null returnType for MethodReference (because we need the MethodReference object to pass it as a context to correctly import ... the return type)

Francois.