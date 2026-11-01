// File:    AppTheme.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App;

/// <summary>Theme options surfaced in settings.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,

    /// <summary>Black and white with strong borders, for people who need maximum contrast.</summary>
    HighContrast,
}
