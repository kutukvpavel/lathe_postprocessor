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
            public int DirectionMultiplier { get; set; } = 0; //0 means indeterminate (for example, for the first motion)

            public Axis(string name, double lash)
            {
                Name = name;
                Lash = lash;
                Reset();
            }

            public void Reset()
            {
                DirectionMultiplier = 0;
                CurrentIdealPosition = 0;
            }

            public void InitializeDirection(double firstMoveCoord)
            {
                CurrentIdealPosition = firstMoveCoord;
                DirectionMultiplier = Math.Sign(firstMoveCoord);
            }

            public bool IsSameDirection(double newPos)
            {
                if (DirectionMultiplier == 0) return true;
                return (CurrentIdealPosition < newPos && DirectionMultiplier > 0) ||
                    (CurrentIdealPosition > newPos && DirectionMultiplier < 0);
            }
        }

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
                CompensateLine(new Parser(line).Parse(), output);
            }
        }

        private void CompensateLine(IEnumerable<Code> codes, TextWriter output)
        {
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
                            }
                            break;
                        case 10:
                            foreach (var arg in gcode.Arguments)
                            {
                                if (arg.Kind == ArgumentKind.L)
                                {
                                    switch (arg.Value)
                                    {
                                        case 20:
                                            foreach (var axis in Axes)
                                            {
                                                axis.Value.CurrentIdealPosition = 0;
                                            }
                                            break;
                                        default:
                                            throw new NotSupportedException("This L argument for G10 is not supported");
                                    }
                                }
                                if (!Axes.ContainsKey(arg.Kind)) continue;
                            }
                            break;
                        case 28:
                            foreach (var axis in Axes)
                            {
                                axis.Value.Reset();
                            }
                            break;
                        default:
                            break;
                    }
                }
                output.Write(code.ToString());
            }
            output.WriteLine();
        }

        private string? GetCountermeasureCommand(Gcode code)
        {
            /* Warning: only support G1 moves
            * The idea:
            *   G1 X1 Y1
                G92 X1.6 Y1.3 <-- Insert (0.6 and 0.3 = backlash)
                G1 X1 Y1 <-- Insert (repeats last G1 command for axes with backlash)
                G1 X-1 Y-1
            */
            List<Argument> argumentsG92 = new List<Argument>(code.Arguments.Count);
            List<Argument> argumentsG1 = new List<Argument>(code.Arguments.Count);
            foreach (Argument arg in code.Arguments)
            {
                if (arg.Kind == ArgumentKind.F)
                {
                    argumentsG1.Add(new Argument(ArgumentKind.F, arg.Value, new Gcodes.Tokens.Span()));
                    continue;
                }
                if (!Axes.ContainsKey(arg.Kind)) continue;
                var axis = Axes[arg.Kind];
                if (axis.DirectionMultiplier == 0)
                {
                    axis.InitializeDirection(arg.Value);
                }
                else if (!axis.IsSameDirection(arg.Value) && (axis.Lash > 0))
                {
                    argumentsG92.Add(new Argument(arg.Kind, axis.CurrentIdealPosition + axis.Lash * axis.DirectionMultiplier,
                        new Gcodes.Tokens.Span()));
                    argumentsG1.Add(new Argument(arg.Kind, axis.CurrentIdealPosition, new Gcodes.Tokens.Span()));
                    axis.DirectionMultiplier = -axis.DirectionMultiplier;
                }
                axis.CurrentIdealPosition = arg.Value;
            }
            if (argumentsG92.Count == 0) return null;
            StringBuilder sb = new StringBuilder();
            Gcode fixCodeG92 = new Gcode(92, argumentsG92, new Gcodes.Tokens.Span());
            Gcode fixCodeG1 = new Gcode(code.Number, argumentsG1, new Gcodes.Tokens.Span());
            sb.AppendLine(fixCodeG92.ToString());
            sb.Append(fixCodeG1.ToString());
            return sb.ToString();
        }
    }

}