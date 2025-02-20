using System;
using CommandLine;

namespace LathePostprocessor
{
  public class Options
  {
    [Option('x', HelpText = "X backlash", Required = true)]
    public double XLash { get; set; } = 0;
    [Option('y', HelpText = "Y backlash", Required = true)]
    public double YLash { get; set; } = 0;
    [Option('z', HelpText = "Z backlash", Required = true)]
    public double ZLash { get; set; } = 0;
    [Option('a', HelpText = "A backlash", Required = true)]
    public double ALash { get; set; } = 0;
    [Option('i', "input", HelpText = "Input file path", Required = false)]
    public string? InputFilePath { get; set; }
    [Option('o', "output", HelpText = "Output file path", Required = false)]
    public string? OutputFilePath { get; set; }
  }

  internal class Program
  {
    static int Main(string[] args)
    {
      Parser.Default.ParseArguments<Options>(args)
                   .WithParsed(o =>
                   {
                     NoLash lash = new NoLash(o.XLash, o.YLash, o.ZLash, o.ALash);
                     using TextReader reader = o.InputFilePath != null ?
                       new StreamReader(o.InputFilePath) : new StreamReader(Console.OpenStandardInput());
                     using TextWriter writer = o.OutputFilePath != null ?
                       new StreamWriter(o.OutputFilePath) : new StreamWriter(Console.OpenStandardOutput());
                     lash.Compensate(reader, writer);
                   });
      return 0;
    }
  }
}