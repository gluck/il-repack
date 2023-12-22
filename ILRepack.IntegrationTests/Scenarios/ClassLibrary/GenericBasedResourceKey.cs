using System.Reflection;
using System.Windows;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

namespace ClassLibrary
{
    public enum ThemesResourceKey
    {
        Background
    }

    public abstract class GenericResourceKey<T> : ResourceKey
    {
        public T ResourceKey { get; set; }
    }

    public class GenericBasedThemeResourceKey : GenericResourceKey<ThemesResourceKey>
    {
        public override Assembly Assembly { get; }
    }
}
