using System;
using System.IO;

namespace edds2png
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "edds2png";
            var converter = new BulkConverter();

            foreach (var arg in args)
            {
                if (Directory.Exists(arg))
                {
                    var files = Directory.EnumerateFiles(arg, "*.edds", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        converter.Add(file);
                    }
                }
                else
                {
                    converter.Add(arg);
                }
            }

            if (converter.CanProcess())
            {
                converter.Process();

                Console.Write(Environment.NewLine);
                Console.Write("Press ENTER to exit...");
                while (Console.ReadKey(true).Key != ConsoleKey.Enter) { };
            }
        }
    }
}
