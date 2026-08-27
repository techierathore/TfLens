namespace TfLens.Components.Shared;

/// <summary>
/// Which chart colour a <c>StatTile</c>'s icon chip uses.
/// </summary>
/// <remarks>
/// The five values map onto TrBlazeUI's own <c>--chart-1..5</c> tokens plus <c>--destructive</c>, so a KPI
/// row picks its accents from the theme rather than from hard-coded colours (UI design §Design system).
/// </remarks>
public enum StatAccent
{
    /// <summary>The primary chart colour — counts and totals.</summary>
    Chart1 = 0,

    /// <summary>The second chart colour — volumes and record counts.</summary>
    Chart2 = 1,

    /// <summary>The third chart colour.</summary>
    Chart3 = 2,

    /// <summary>The fourth chart colour — times and ages.</summary>
    Chart4 = 3,

    /// <summary>The fifth chart colour.</summary>
    Chart5 = 4,

    /// <summary>The destructive colour — errors and failures.</summary>
    Destructive = 5
}
