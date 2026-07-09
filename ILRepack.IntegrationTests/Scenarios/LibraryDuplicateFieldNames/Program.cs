using System;

public class Program
{
    [STAThread]
    public static int Main()
    {
        try
        {
            new Library.Test().TestMethod();
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
    }
}