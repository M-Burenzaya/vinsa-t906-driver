namespace VinsaStudio.Core;

public static class FeatureProtocol
{
    private static byte Pair(int first, int second) => (byte)(((first >> 8) & 15) | ((second >> 4) & 240));
    public static byte[][] Plan(TabletSettings settings, ushort version = 0)
    {
        settings.Validate();
        var t = settings.TabletArea;
        var s = settings.ScreenArea;
        int tw = t.Right - t.Left, th = t.Bottom - t.Top;
        int sw = s.Right - s.Left, sh = s.Bottom - s.Top;
        return
        [
            [8, 7, Pair(t.Left, t.Top), (byte)t.Left, (byte)t.Top, Pair(tw, th), (byte)tw, (byte)th],
            [8, 6, (byte)(settings.InkMode || version > 0x1802 ? 1 : 0), (byte)(settings.LeftHandMode ? 3 : 0), 0, 0, 0, 0],
            [8, (byte)(settings.InkMode ? 3 : 1), (byte)s.Left, (byte)sw, Pair(s.Left, sw), (byte)s.Top, (byte)sh, Pair(s.Top, sh)]
        ];
    }
}
