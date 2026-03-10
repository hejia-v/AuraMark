namespace AuraMark.Core.Text;

public sealed record EditorOptions(
    int TabSize = 4,
    bool UseSoftWrap = true,
    bool ShowParagraphSpacing = true,
    double PageWidth = 860,
    string ThemeName = "Aura");
