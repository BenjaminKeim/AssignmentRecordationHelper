using System.IO;
using System.IO.Compression;
using System.Text;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace AssignmentRecordationPrep.Services;

/// <summary>
/// Self-contained PDF parser for extracting XFA datasets XML from USPTO ADS forms.
/// Lifted verbatim from FilingReceiptReview — same two-tier approach:
///   1. Structured: navigate the PDF object tree (fast, precise)
///   2. Brute-force: decompress every FlateDecode stream and scan for XFA markers
/// </summary>
internal static class PdfXfaExtractor
{
    private sealed class Ref { public int Num; public Ref(int n) { Num = n; } }

    private sealed class XrefEntry
    {
        public bool InObjStm;
        public int  Offset;
        public int  ObjStmNum;
        public int  ObjStmIdx;
    }

    public static bool HasXfa(byte[] file)
    {
        try
        {
            var xref = new Dictionary<int, XrefEntry>();
            var trailer = BuildXref(file, xref);
            if (trailer != null)
            {
                var acroForm = NavToAcroForm(file, xref, trailer);
                if (acroForm != null && acroForm.ContainsKey("/XFA"))
                    return true;
            }
        }
        catch { }
        return ScanStreamsForXfa(file);
    }

    public static string? ExtractDatasetsXml(byte[] file)
    {
        try
        {
            var xref = new Dictionary<int, XrefEntry>();
            var trailer = BuildXref(file, xref);
            if (trailer != null)
            {
                var acroForm = NavToAcroForm(file, xref, trailer);
                if (acroForm != null)
                {
                    var xfaArr = GetArray(file, xref, acroForm, "/XFA");
                    if (xfaArr != null)
                        for (int i = 0; i + 1 < xfaArr.Count; i++)
                            if (xfaArr[i] is string s &&
                                (s == "datasets" || s == "/datasets") &&
                                xfaArr[i + 1] is Ref r)
                            {
                                var bytes = StreamBytesForObj(file, xref, r.Num);
                                if (bytes != null) return Encoding.UTF8.GetString(bytes);
                            }
                }
            }
        }
        catch { }
        return ScanStreamsForDatasets(file);
    }

    private static bool ScanStreamsForXfa(byte[] file)
        => ScanDeflatedStreams(file, (buf, len) => ByteContains(buf, len, "xdp:xdp"u8));

    private static string? ScanStreamsForDatasets(byte[] file)
    {
        var candidates = new List<string>();
        ScanDeflatedStreams(file, (buf, len) =>
        {
            var ns   = "<xfa:datasets"u8;
            var bare = "<datasets"u8;
            int idx = ByteIndexOf(buf, len, ns);
            if (idx < 0) idx = ByteIndexOf(buf, len, bare);
            if (idx < 0) return false;
            candidates.Add(Encoding.UTF8.GetString(buf, idx, len - idx));
            return false;
        });
        return candidates.Count == 0 ? null : candidates.OrderByDescending(s => s.Length).First();
    }

    private static bool ScanDeflatedStreams(byte[] file, Func<byte[], int, bool> predicate)
    {
        ReadOnlySpan<byte> keyword = "stream"u8;
        for (int i = 0; i < file.Length - 7; i++)
        {
            if (file[i] != 's') continue;
            if (!Eq(file, i, keyword)) continue;
            int pos = i + 6;
            if (pos < file.Length && file[pos] == '\r') pos++;
            if (pos < file.Length && file[pos] == '\n') pos++;
            if (pos >= file.Length) continue;
            foreach (int skip in (int[])[2, 0])
            {
                int start = pos + skip;
                if (start >= file.Length) continue;
                try
                {
                    int maxIn = Math.Min(file.Length - start, 8 * 1024 * 1024);
                    using var outMs = new MemoryStream();
                    using (var ds = new DeflateStream(
                        new MemoryStream(file, start, maxIn, writable: false),
                        CompressionMode.Decompress, leaveOpen: false))
                        ds.CopyTo(outMs);
                    byte[] buf = outMs.ToArray();
                    if (buf.Length == 0) continue;
                    if (predicate(buf, buf.Length)) return true;
                    break;
                }
                catch { }
            }
        }
        return false;
    }

    private static bool ByteContains(byte[] buf, int len, ReadOnlySpan<byte> pattern)
        => ByteIndexOf(buf, len, pattern) >= 0;

    private static int ByteIndexOf(byte[] buf, int len, ReadOnlySpan<byte> pattern)
    {
        int limit = len - pattern.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool ok = true;
            for (int j = 0; j < pattern.Length; j++)
                if (buf[i + j] != pattern[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    private static Dict? NavToAcroForm(byte[] file, Dictionary<int, XrefEntry> xref, Dict trailer)
    {
        var catalog = ResolveDict(file, xref, trailer, "/Root");
        return catalog == null ? null : ResolveDict(file, xref, catalog, "/AcroForm");
    }

    private static Dict? ResolveDict(byte[] file, Dictionary<int, XrefEntry> xref, Dict parent, string key)
    {
        if (!parent.TryGetValue(key, out var v)) return null;
        if (v is Ref r) return Resolve<Dict>(file, xref, r.Num);
        return v as Dict;
    }

    private static List<object?>? GetArray(byte[] file, Dictionary<int, XrefEntry> xref, Dict parent, string key)
    {
        if (!parent.TryGetValue(key, out var v)) return null;
        if (v is Ref r) return Resolve<List<object?>>(file, xref, r.Num);
        return v as List<object?>;
    }

    private static Dict? BuildXref(byte[] file, Dictionary<int, XrefEntry> xref)
    {
        int pos = FindStartXref(file);
        if (pos < 0) return null;
        Dict? trailer = null;
        int? next = pos;
        while (next.HasValue)
        {
            int p = SkipWs(file, next.Value);
            next = null;
            Dict? sec; int? prev;
            if (p < file.Length && file[p] == 'x')
                (sec, prev) = TradXref(file, p, xref);
            else
                (sec, prev) = StreamXref(file, p, xref);
            trailer ??= sec;
            next = prev;
        }
        return trailer;
    }

    private static int FindStartXref(byte[] file)
    {
        int from = Math.Max(0, file.Length - 1024);
        for (int i = file.Length - 9; i >= from; i--)
            if (file[i] == 's' && Eq(file, i, "startxref"u8))
                return (int)ReadNum(file, SkipWs(file, i + 9), out _);
        return -1;
    }

    private static (Dict? T, int? Prev) TradXref(byte[] file, int pos, Dictionary<int, XrefEntry> xref)
    {
        pos += 4;
        while (pos < file.Length)
        {
            pos = SkipWs(file, pos);
            if (Eq(file, pos, "trailer"u8)) break;
            int first = (int)ReadNum(file, pos, out pos);
            pos = SkipWs(file, pos);
            int count = (int)ReadNum(file, pos, out pos);
            pos = SkipWs(file, pos);
            for (int j = 0; j < count && pos + 20 <= file.Length; j++, pos += 20)
            {
                int offset = (int)ReadNum(file, pos, out _);
                int objNum = first + j;
                if (file[pos + 17] == 'n' && !xref.ContainsKey(objNum))
                    xref[objNum] = new XrefEntry { Offset = offset };
            }
        }
        pos = SkipWs(file, pos);
        if (Eq(file, pos, "trailer"u8)) pos += 7;
        var d = ParseDict(file, SkipWs(file, pos), out _);
        return (d, GetPrev(d));
    }

    private static (Dict? T, int? Prev) StreamXref(byte[] file, int pos, Dictionary<int, XrefEntry> xref)
    {
        pos = SkipWs(file, pos);
        ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
        ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
        if (Eq(file, pos, "obj"u8)) pos += 3;
        pos = SkipWs(file, pos);
        var dict = ParseDict(file, pos, out pos);
        if (dict == null) return (null, null);
        pos = SkipWs(file, pos);
        if (!Eq(file, pos, "stream"u8)) return (dict, null);
        pos += 6;
        if (pos < file.Length && file[pos] == '\r') pos++;
        if (pos < file.Length && file[pos] == '\n') pos++;
        int len = dict.TryGetValue("/Length", out var lv) && lv is long ll ? (int)ll : 0;
        int rawLen = Math.Min(len, file.Length - pos);
        var raw = new byte[rawLen];
        Array.Copy(file, pos, raw, 0, rawLen);
        byte[] data = Inflate(dict, raw);
        int w0 = 1, w1 = 4, w2 = 2;
        if (dict.TryGetValue("/W", out var wv) && wv is List<object?> wa && wa.Count >= 3)
        { w0 = ToI(wa[0]); w1 = ToI(wa[1]); w2 = ToI(wa[2]); }
        var subs = new List<(int F, int C)>();
        if (dict.TryGetValue("/Index", out var iv) && iv is List<object?> ia)
            for (int k = 0; k + 1 < ia.Count; k += 2) subs.Add((ToI(ia[k]), ToI(ia[k + 1])));
        else if (dict.TryGetValue("/Size", out var sv) && sv is long sl)
            subs.Add((0, (int)sl));
        int es = w0 + w1 + w2, dp = 0;
        foreach (var (f, c) in subs)
            for (int j = 0; j < c && dp + es <= data.Length; j++, dp += es)
            {
                int type = ReadB(data, dp, w0), f1 = ReadB(data, dp + w0, w1), f2 = ReadB(data, dp + w0 + w1, w2);
                int n = f + j;
                if (!xref.ContainsKey(n))
                {
                    if      (type == 1) xref[n] = new XrefEntry { Offset = f1 };
                    else if (type == 2) xref[n] = new XrefEntry { InObjStm = true, ObjStmNum = f1, ObjStmIdx = f2 };
                }
            }
        return (dict, GetPrev(dict));
    }

    private static int? GetPrev(Dict? d)
    {
        if (d != null && d.TryGetValue("/Prev", out var v) && v is long l) return (int)l;
        return null;
    }

    private static T? Resolve<T>(byte[] file, Dictionary<int, XrefEntry> xref, int objNum) where T : class
        => GetObj(file, xref, objNum) as T;

    private static object? GetObj(byte[] file, Dictionary<int, XrefEntry> xref, int objNum)
    {
        if (!xref.TryGetValue(objNum, out var e)) return null;
        if (!e.InObjStm)
        {
            int pos = SkipWs(file, e.Offset);
            ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
            ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
            if (Eq(file, pos, "obj"u8)) pos += 3;
            return ParseVal(file, SkipWs(file, pos), out _);
        }
        var (stm, firstOffset) = GetObjStmBytes(file, xref, e.ObjStmNum);
        return stm == null ? null : ObjFromStm(stm, e.ObjStmIdx, firstOffset);
    }

    private static (byte[]? Bytes, int First) GetObjStmBytes(byte[] file, Dictionary<int, XrefEntry> xref, int objNum)
    {
        if (!xref.TryGetValue(objNum, out var e) || e.InObjStm) return (null, 0);
        int pos = SkipWs(file, e.Offset);
        ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
        ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
        if (Eq(file, pos, "obj"u8)) pos += 3;
        pos = SkipWs(file, pos);
        var dict = ParseDict(file, pos, out pos);
        if (dict == null) return (null, 0);
        int firstOffset = dict.TryGetValue("/First", out var fv) && fv is long fl ? (int)fl : 0;
        pos = SkipWs(file, pos);
        if (!Eq(file, pos, "stream"u8)) return (null, 0);
        pos += 6;
        if (pos < file.Length && file[pos] == '\r') pos++;
        if (pos < file.Length && file[pos] == '\n') pos++;
        int len = dict.TryGetValue("/Length", out var lv) && lv is long ll ? (int)ll : 0;
        int rawLen = Math.Min(len, file.Length - pos);
        var raw = new byte[rawLen];
        Array.Copy(file, pos, raw, 0, rawLen);
        return (Inflate(dict, raw), firstOffset);
    }

    private static byte[]? StreamBytesForObj(byte[] file, Dictionary<int, XrefEntry> xref, int objNum)
    {
        if (!xref.TryGetValue(objNum, out var e) || e.InObjStm) return null;
        int pos = SkipWs(file, e.Offset);
        ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
        ReadNum(file, pos, out pos); pos = SkipWs(file, pos);
        if (Eq(file, pos, "obj"u8)) pos += 3;
        pos = SkipWs(file, pos);
        var dict = ParseDict(file, pos, out pos);
        if (dict == null) return null;
        pos = SkipWs(file, pos);
        if (!Eq(file, pos, "stream"u8)) return null;
        pos += 6;
        if (pos < file.Length && file[pos] == '\r') pos++;
        if (pos < file.Length && file[pos] == '\n') pos++;
        int len = dict.TryGetValue("/Length", out var lv) && lv is long ll ? (int)ll : 0;
        int rawLen = Math.Min(len, file.Length - pos);
        var raw = new byte[rawLen];
        Array.Copy(file, pos, raw, 0, rawLen);
        return Inflate(dict, raw);
    }

    private static object? ObjFromStm(byte[] stm, int idx, int firstOffset)
    {
        int pos = 0;
        var offs = new List<int>();
        while (true)
        {
            int p = SkipWs(stm, pos);
            if (p >= stm.Length || !IsDigit(stm[p])) break;
            ReadNum(stm, p, out p);
            p = SkipWs(stm, p);
            offs.Add((int)ReadNum(stm, p, out p));
            pos = p;
        }
        if (idx >= offs.Count) return null;
        int objPos = firstOffset + offs[idx];
        return objPos >= stm.Length ? null : ParseVal(stm, objPos, out _);
    }

    private static byte[] Inflate(Dict dict, byte[] raw)
    {
        string? f = null;
        if (dict.TryGetValue("/Filter", out var fv))
        {
            if (fv is string s) f = s;
            else if (fv is List<object?> fa && fa.Count > 0) f = fa[0] as string;
        }
        if (f is "/FlateDecode" or "/Fl")
        {
            foreach (int skip in (int[])[2, 0])
            {
                int start = skip;
                if (start >= raw.Length) continue;
                try
                {
                    using var ms = new MemoryStream();
                    using var ds = new DeflateStream(
                        new MemoryStream(raw, start, raw.Length - start, writable: false),
                        CompressionMode.Decompress, leaveOpen: false);
                    ds.CopyTo(ms);
                    byte[] result = ms.ToArray();
                    if (result.Length > 0) return result;
                }
                catch { }
            }
        }
        return raw;
    }

    private static Dict? ParseDict(byte[] d, int pos, out int end)
    {
        pos = SkipWs(d, pos); end = pos;
        if (pos + 1 >= d.Length || d[pos] != '<' || d[pos + 1] != '<') return null;
        pos += 2;
        var dict = new Dict();
        while (true)
        {
            pos = SkipWs(d, pos);
            if (pos + 1 < d.Length && d[pos] == '>' && d[pos + 1] == '>') { end = pos + 2; return dict; }
            if (pos >= d.Length) break;
            if (ParseVal(d, pos, out pos) is not string key) break;
            dict[key] = ParseVal(d, SkipWs(d, pos), out pos);
        }
        end = pos; return dict.Count > 0 ? dict : null;
    }

    private static object? ParseVal(byte[] d, int pos, out int end)
    {
        pos = SkipWs(d, pos); end = pos;
        if (pos >= d.Length) return null;
        byte b = d[pos];
        if (b == '<' && pos + 1 < d.Length && d[pos + 1] == '<') return ParseDict(d, pos, out end);
        if (b == '[')
        {
            pos++;
            var list = new List<object?>();
            while (true)
            {
                pos = SkipWs(d, pos);
                if (pos >= d.Length || d[pos] == ']') { end = pos + 1; return list; }
                list.Add(ParseVal(d, pos, out pos));
            }
        }
        if (b == '/')
        {
            pos++;
            var sb = new System.Text.StringBuilder("/");
            while (pos < d.Length && !IsDelim(d[pos]) && !IsWs(d[pos])) sb.Append((char)d[pos++]);
            end = pos; return sb.ToString();
        }
        if (b == '(')
        {
            pos++; int depth = 1;
            var sbStr = new System.Text.StringBuilder();
            while (pos < d.Length && depth > 0)
            {
                byte c = d[pos++];
                if (c == '\\') { if (pos < d.Length) sbStr.Append((char)d[pos++]); continue; }
                if      (c == '(') { depth++; sbStr.Append('('); }
                else if (c == ')') { if (--depth == 0) break; sbStr.Append(')'); }
                else sbStr.Append((char)c);
            }
            end = pos; return sbStr.ToString();
        }
        if (b == '<') { while (pos < d.Length && d[pos] != '>') pos++; end = pos + 1; return ""; }
        if (IsDigit(b) || b == '-' || b == '+')
        {
            long n1 = ReadNum(d, pos, out int p2);
            int p2w = SkipWs(d, p2);
            if (p2w < d.Length && IsDigit(d[p2w]))
            {
                ReadNum(d, p2w, out int p3);
                int p3w = SkipWs(d, p3);
                if (p3w < d.Length && d[p3w] == 'R' &&
                    (p3w + 1 >= d.Length || IsWs(d[p3w + 1]) || IsDelim(d[p3w + 1])))
                { end = p3w + 1; return new Ref((int)n1); }
            }
            end = p2; return n1;
        }
        if (Eq(d, pos, "true"u8))  { end = pos + 4; return true; }
        if (Eq(d, pos, "false"u8)) { end = pos + 5; return false; }
        if (Eq(d, pos, "null"u8))  { end = pos + 4; return null; }
        end = pos + 1; return null;
    }

    private static long ReadNum(byte[] d, int pos, out int end)
    {
        pos = SkipWs(d, pos);
        bool neg = pos < d.Length && d[pos] == '-';
        if (neg || (pos < d.Length && d[pos] == '+')) pos++;
        long v = 0;
        while (pos < d.Length && IsDigit(d[pos])) v = v * 10 + (d[pos++] - '0');
        end = pos; return neg ? -v : v;
    }

    private static int ReadB(byte[] d, int pos, int w)
    {
        int v = 0;
        for (int i = 0; i < w && pos + i < d.Length; i++) v = (v << 8) | d[pos + i];
        return v;
    }

    private static int SkipWs(byte[] d, int pos)
    { while (pos < d.Length && IsWs(d[pos])) pos++; return pos; }

    private static bool IsWs(byte b)    => b is 32 or 9 or 13 or 10 or 12;
    private static bool IsDigit(byte b) => b >= '0' && b <= '9';
    private static bool IsDelim(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or
             (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static bool Eq(byte[] d, int pos, ReadOnlySpan<byte> pat)
    {
        if (pos + pat.Length > d.Length) return false;
        for (int i = 0; i < pat.Length; i++) if (d[pos + i] != pat[i]) return false;
        return true;
    }

    private static int ToI(object? v) => v is long l ? (int)l : 0;
}
