The `Library.dll` has the source code below:

```csharp
using System;
using System.Linq;
using System.Reflection;

namespace Library
{
    public class Test
    {
        private string test1 = "default";
        private int test2 = 1;
        private double test3 = 1.0;
        public void TestMethod()
        {
            test1 = "changed";
            test2 = 42;
            test3 = 3.1415;

            Console.WriteLine($"{test1}, {test2}, {test3}");

            var fields = this.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic |BindingFlags.Instance);
            foreach (var field in fields)
            {
                Console.WriteLine($"Field: {field.Name}, Type: {field.FieldType}, Value: {field.GetValue(this)}");
            }
            if (fields.Count() != 3)
            {
                throw new InvalidOperationException("Unexpected number of fields.");
            }
            if (test1 != "changed")
            {
                throw new InvalidOperationException($"Unexpected string value: {test1}");
            }
            if (test2 != 42)
            {
                throw new InvalidOperationException($"Unexpected int value: {test2}");
            }
            if (test3 != 3.1415)
            {
                throw new InvalidOperationException($"Unexpected double value: {test3}");
            }
        }
    }
}
```

After build, the `Library.dll` passed to the obfuscation process that renames the private fields to the same name as `A`.