using System.Text;
using Gcodes;
using Gcodes.Ast;

namespace LathePostprocessor
{
  public sealed class NoLash
  {
    // Encapsulates current axis state
    class Axis
    {
      public string Name { get; }
      public double Lash { get; }
      public double CurrentIdealPosition { get; set; }
      public int DirectionMultiplier { get; set; }

      public Axis(string name, double lash)
      {
        Name = name;
        Lash = lash;
        Reset();
      }

      public void Reset()
      {
        DirectionMultiplier = 1;
        CurrentIdealPosition = 0;
      }

      public bool IsSameDirection(double newPos)
      {
        return (CurrentIdealPosition < newPos && DirectionMultiplier > 0) ||
          (CurrentIdealPosition > newPos && DirectionMultiplier < 0);
      }
    }

    bool ModalStateAbsolute = true;

    Axis AxisX;
    Axis AxisY;
    Axis AxisZ;
    Axis AxisA;

    Dictionary<ArgumentKind, Axis> Axes;

    public NoLash(double x, double y, double z, double a)
    {
      // divide by 2 to compensate for 2 directions of movement
      // (forward and backward)
      AxisX = new Axis("X", x);
      AxisY = new Axis("Y", y);
      AxisZ = new Axis("Z", z);
      AxisA = new Axis("A", a);
      Axes = new Dictionary<ArgumentKind, Axis>{
      { ArgumentKind.X, AxisX },
      { ArgumentKind.Y, AxisY },
      { ArgumentKind.Z, AxisZ },
      { ArgumentKind.A, AxisA }
    };
    }

    public void Compensate(TextReader input, TextWriter output)
    {
      string? line;
      while ((line = input.ReadLine()) != null)
      {
        var codes = new Parser(line).Parse();
        bool firstCommand = true;
        foreach (var code in codes)
        {
          Gcode? gcode = code as Gcode;
          if (gcode != null)
          {
            switch (gcode.Number)
            {
              case 0:
              case 1:
                var cmd = GetCountermeasureCommand(gcode);
                if (cmd != null)
                {
                  output.WriteLine(cmd);
                  firstCommand = true;
                }
                break;
              case 28:
                foreach (var axis in Axes)
                {
                  axis.Value.Reset();
                }
                break;
              case 90:
                ModalStateAbsolute = true;
                break;
              case 91:
                ModalStateAbsolute = false;
                break;
              default:
                break;
            }
          }
          if (firstCommand)
          {
            firstCommand = false;
          }
          else
          {
            output.Write(' ');
          }
          output.Write(code.ToString());
        }
        output.WriteLine();
      }
    }

    private string? GetCountermeasureCommand(Gcode code)
    {
      List<Argument> arguments = new List<Argument>(code.Arguments.Count);
      double? feedrate = null;
      foreach (Argument arg in code.Arguments)
      {
        if (arg.Kind == ArgumentKind.FeedRate)
        {
          feedrate = arg.Value;
          continue;
        }
        if (!Axes.ContainsKey(arg.Kind)) continue;
        var axis = Axes[arg.Kind];
        if (!axis.IsSameDirection(arg.Value) && (axis.Lash > 0))
        {
          axis.DirectionMultiplier = -axis.DirectionMultiplier;
          arguments.Add(new Argument(arg.Kind, axis.Lash * axis.DirectionMultiplier, arg.Span));
        }
        axis.CurrentIdealPosition = arg.Value;
      }
      if (arguments.Count == 0) return null;
      StringBuilder sb = new StringBuilder(Environment.NewLine);
      if (ModalStateAbsolute) sb.AppendLine("G91");
      sb.Append($"G{code.Number}");
      foreach (Argument arg in arguments)
      {
        sb.Append($" {Axes[arg.Kind].Name}{arg.Value:F2}");
      }
      if (feedrate != null) sb.Append($" F{feedrate:F2}");
      if (ModalStateAbsolute)
      {
        sb.AppendLine();
        sb.Append("G90");
      }
      return sb.ToString();
    }
  }

}