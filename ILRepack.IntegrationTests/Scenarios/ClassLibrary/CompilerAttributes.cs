#nullable enable

class A
{
    static object? F1;
    static object? F2;
    static nint F3;
}

ref struct S { }

class B
{
    void M1(scoped in S s) { }

    void M2<T>() where T : unmanaged { }
}
