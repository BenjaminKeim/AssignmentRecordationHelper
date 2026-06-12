namespace AssignmentRecordationHelper.Models;

public class AdsInventor
{
    public string First  { get; set; } = "";
    public string Middle { get; set; } = "";
    public string Last   { get; set; } = "";

    public string FullName =>
        string.Join(" ", new[] { First, Middle, Last }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
