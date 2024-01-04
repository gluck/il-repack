#nullable enable

class A1
{
    static object? F1;
    static object? F2;
    static nint F3;
}

ref struct S1 { }

class B1
{
    void M1(scoped in S1 s) { }

    void M2<T>() where T : unmanaged { }
}
