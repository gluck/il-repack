
namespace ILRepacking
{
    public interface ICommandLine
    {
        string[] OtherAguments { get; }

        bool Modifier(string modifier);

        string Option(string name);

        bool HasOption(string name);

        string[] Options(string name);

        bool OptionBoolean(string name, bool def);

        int OptionsCount { get; }

        bool HasNoOptions { get; }
    }
}
