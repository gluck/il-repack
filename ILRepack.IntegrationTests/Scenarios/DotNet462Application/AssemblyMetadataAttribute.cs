// Force this to be in the same namespace as System.Runtime, since this seem to trigger an issue because of the forwarded type vs defined type
namespace System.Reflection
{
    [AttributeUsage(AttributeTargets.All)]
    internal class AssemblyMetadataAttribute : Attribute
    {
        public AssemblyMetadataAttribute(string key, string value)
        {
        }
    }
}