using System.Runtime.InteropServices;
using System.Text;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>A [Fact] that runs only where the real msvcr100.dll (VS2010 CRT, x64) can be called: a 64-bit Windows process with
/// C:\Windows\System32\msvcr100.dll present (the Visual C++ 2010 x64 redistributable, which Kenshi installs). Elsewhere it is skipped
/// with the reason, so the suite still passes. The DLL must be the build the emulation was measured on, 10.00.40219.325 (VS2010 SP1,
/// the last msvcr100); another build is skipped with the version it found.</summary>
public sealed class Msvcr100FactAttribute : FactAttribute
{
    public const string DllPath = @"C:\Windows\System32\msvcr100.dll";

    public Msvcr100FactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            Skip = "needs a 64-bit Windows process to call msvcr100.dll";
        else if (!File.Exists(DllPath))
            Skip = DllPath + " is not installed (Visual C++ 2010 x64 redistributable)";
        else
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(DllPath);
            if (info.FileMajorPart != 10 || info.FileMinorPart != 0 || info.FileBuildPart != 40219 || info.FilePrivatePart != 325)
                Skip = $"{DllPath} is version {info.FileVersion} ({info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}); " +
                    "the emulation was measured on 10.00.40219.325";
        }
    }
}

/// <summary>Differential test of <see cref="Msvcr100Format"/> (Core's production printf of doubles) against the real msvcr100
/// <c>_vsnprintf</c>, on every format Core prints (%g, %.10g, %.17g, %.0f, %.1f, %.2f, %.3f, %.4f, %.6f) plus %e: a fixed-seed random set,
/// the probe's hard corners (powers of ten and their neighbours, ties at every scale, exact binary fractions, subnormals, short decimals,
/// values near 2^60) and the non-finite spellings. The x64 va_list is a pointer to 8-byte slots, so the double is passed as its bits in a
/// long[].</summary>
public class Msvcr100FormatDllTests
{
    [DllImport(Msvcr100FactAttribute.DllPath, EntryPoint = "_vsnprintf", CallingConvention = CallingConvention.Cdecl)]
    static extern int VsnPrintf(byte[] buffer, nuint count, byte[] format, long[] args);

    static readonly byte[] Buffer = new byte[1024];

    static string Dll(string format, double value)
    {
        byte[] f = Encoding.ASCII.GetBytes(format + "\0");
        int n = VsnPrintf(Buffer, (nuint)(Buffer.Length - 1), f, new[] { BitConverter.DoubleToInt64Bits(value), 0L });
        return n < 0 ? "<error " + n + ">" : Encoding.ASCII.GetString(Buffer, 0, n);
    }

    // "%.*x" with the precision in its own int slot, then the double
    static string DllStar(string format, int precision, double value)
    {
        byte[] f = Encoding.ASCII.GetBytes(format + "\0");
        var big = new byte[4096];
        int n = VsnPrintf(big, (nuint)(big.Length - 1), f, new[] { (long)precision, BitConverter.DoubleToInt64Bits(value), 0L });
        return n < 0 ? "<error " + n + ">" : Encoding.ASCII.GetString(big, 0, n);
    }

    // (printf format, Core formatter)
    static readonly (string fmt, Func<double, string> core)[] Formats =
    {
        ("%g", v => Msvcr100Format.G(v, 6)), ("%.10g", v => Msvcr100Format.G(v, 10)), ("%.17g", v => Msvcr100Format.G(v, 17)),
        ("%.0f", v => Msvcr100Format.F(v, 0)), ("%.1f", v => Msvcr100Format.F(v, 1)), ("%.2f", v => Msvcr100Format.F(v, 2)),
        ("%.3f", v => Msvcr100Format.F(v, 3)), ("%.4f", v => Msvcr100Format.F(v, 4)), ("%.6f", v => Msvcr100Format.F(v, 6)),
        ("%e", v => Msvcr100Format.E(v, 6)),
    };

    // xorshift64* (test-only generator, fixed seed)
    sealed class Rng
    {
        ulong _s;
        public Rng(ulong seed) { _s = seed; }
        public ulong Next() { _s ^= _s >> 12; _s ^= _s << 25; _s ^= _s >> 27; return _s * 2685821657736338717UL; }
        public int Below(int n) => (int)(Next() % (ulong)n);
        public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));
    }

    static List<double> RandomSet(int n)
    {
        var r = new Rng(20261112);
        var vals = new List<double>(n);
        while (vals.Count < n)
        {
            double v; int kind = r.Below(100);
            if (kind < 25) { v = BitConverter.Int64BitsToDouble((long)(r.Next() & 0x7FFFFFFFFFFFFFFFUL)); if (!double.IsFinite(v)) continue; }
            else if (kind < 50) v = (1.0 + r.Unit()) * Math.Pow(2, r.Below(141) - 70);
            else if (kind < 70) v = r.Below(10_000_000) / Math.Pow(10, r.Below(9));
            else if (kind < 85) v = (r.Below(1_000_000) + 0.5) / Math.Pow(2, r.Below(11));
            else v = (double)(long)(r.Next() >> 11) * Math.Pow(10, r.Below(18) - 12);
            vals.Add((r.Next() & 1) == 0 ? -v : v);
        }
        return vals;
    }

    static List<double> HardSet()
    {
        var r = new Rng(99);
        var h = new List<double>();
        for (int k = -320; k <= 308; k++) { double p = Math.Pow(10, k); if (p == 0 || !double.IsFinite(p)) continue; h.Add(p); h.Add(Math.BitIncrement(p)); h.Add(Math.BitDecrement(p)); }
        foreach (double m in new[] { 9.5, 9.95, 9.995, 9.9999995, 9.99999995, 0.5, 1.5, 2.5, 9.99999999999999995, 1.125, 0.125, 0.0625 })
            for (int k = -300; k <= 300; k += 3) { double v = m * Math.Pow(10, k); h.Add(v); h.Add(Math.BitIncrement(v)); h.Add(Math.BitDecrement(v)); }
        for (int i = 0; i < 3000; i++) h.Add(1_000_000_000_000_000L + (long)(r.Next() % 1_200_000_000_000_000UL) + ((r.Next() & 1) == 0 ? 0.25 : 0.75));
        for (int i = 0; i < 3000; i++) h.Add(BitConverter.Int64BitsToDouble((long)(r.Next() % ((1UL << 52) - 1)) + 1));   // subnormals
        for (int i = 0; i < 5000; i++) h.Add(((long)(r.Next() % (1UL << 40)) * 2 + 1) / Math.Pow(2, 1 + r.Below(29)));      // exact binary ties
        for (int i = 0; i < 5000; i++) h.Add(Math.Round(r.Unit() * 1000, r.Below(9)));                                       // short decimals
        foreach (double v in new[] { 0.0, double.Epsilon, double.MaxValue, 0.5000000000000001, 0.49999999999999994, 2.5000000000000004,
                     2.4999999999999996, 11115.125, 100.5, 1.0 / 3.0, 2.675, 1.005, 24000000.0, 3.4028234663852886e38 })
            h.Add(v);
        return h.Select((v, i) => (i & 1) == 0 ? v : -v).Concat(h).ToList();
    }

    static void Compare(IEnumerable<double> values, string what)
    {
        int checkedCount = 0, mismatches = 0;
        var examples = new List<string>();
        foreach (double v in values)
            foreach (var (fmt, core) in Formats)
            {
                string expected = Dll(fmt, v), actual = core(v);
                ++checkedCount;
                if (expected != actual)
                {
                    ++mismatches;
                    if (examples.Count < 10) examples.Add($"{fmt} {v:R} (0x{BitConverter.DoubleToInt64Bits(v):X16}): dll `{expected}` core `{actual}`");
                }
            }
        Assert.True(mismatches == 0, $"{what}: {mismatches} of {checkedCount} differ:\n" + string.Join("\n", examples));
    }

    [Msvcr100Fact]
    public void Random_values_match_the_dll() => Compare(RandomSet(50_000), "random");

    [Msvcr100Fact]
    public void Hard_values_match_the_dll() => Compare(HardSet(), "hard");

    [Msvcr100Fact]
    public void Non_finite_spellings_match_the_dll()
    {
        long[] patterns =
        {
            0x7FF0000000000000, unchecked((long)0xFFF0000000000000),                      // +-infinity
            unchecked((long)0xFFF8000000000000), 0x7FF8000000000000,                      // indefinite (double.NaN) and its positive twin
            0x7FF8000000000001, unchecked((long)0xFFF8000000000001), 0x7FFFFFFFFFFFFFFF,  // quiet NaNs with a payload
            0x7FF0000000000001, unchecked((long)0xFFF0000000000001), 0x7FF4000000000000,  // signalling NaNs
        };
        var formats = new List<(string, Func<double, string>)>();
        for (int p = 0; p <= 8; p++)
        {
            int q = p;
            formats.Add(($"%.{p}g", v => Msvcr100Format.G(v, q)));
            formats.Add(($"%.{p}f", v => Msvcr100Format.F(v, q)));
            formats.Add(($"%.{p}e", v => Msvcr100Format.E(v, q)));
        }
        int mismatches = 0;
        var examples = new List<string>();
        foreach (long bits in patterns)
        {
            double v = BitConverter.Int64BitsToDouble(bits);
            foreach (var (fmt, core) in formats.Concat(Formats))
            {
                string expected = Dll(fmt, v), actual = core(v);
                if (expected != actual) { ++mismatches; if (examples.Count < 20) examples.Add($"{fmt} 0x{bits:X16}: dll `{expected}` core `{actual}`"); }
            }
        }
        Assert.True(mismatches == 0, $"{mismatches} non-finite mismatches:\n" + string.Join("\n", examples));
        // the spellings the CRT prints (table_report.md)
        Assert.Equal("-1.#IND", Dll("%g", double.NaN));
        Assert.Equal("1.#INF00", Dll("%.6f", double.PositiveInfinity));
        Assert.Equal("1.#J", Dll("%.2f", double.PositiveInfinity));
    }

    [Msvcr100Fact]
    public void Precision_is_capped_at_512()
    {
        foreach (double v in new[] { 0.1, 1.0 / 3.0, 1e-300, 5e-324, 123456.789, -2.5, 1e300, double.PositiveInfinity, double.NaN })
            foreach (int p in new[] { 511, 512, 513, 1000 })
            {
                Assert.Equal(DllStar("%.*f", p, v), Msvcr100Format.F(v, p));
                Assert.Equal(DllStar("%.*e", p, v), Msvcr100Format.E(v, p));
                Assert.Equal(DllStar("%.*g", p, v), Msvcr100Format.G(v, p));
            }
        Assert.Equal(514, DllStar("%.*f", 1000, 0.1).Length);   // "0." + 512 decimals
        Assert.Equal(514, Msvcr100Format.F(0.1, 1000).Length);
    }

    [Msvcr100Fact]
    public void Core_production_formatters_are_msvcr100()
    {
        foreach (double v in new[] { 1.125, 2.5, 1e10, 11115.125, 0.1, 1234567.0, double.NaN })
        {
            Assert.Equal(Dll("%.2f", v), CMath.FormatF(v, 2));
            Assert.Equal(Dll("%.0f", v), CMath.FormatF(v, 0));
            Assert.Equal(Dll("%g", v), CMath.FormatG(v));
            Assert.Equal(Dll("%.17g", v), CMath.FormatG(v, 17));
        }
    }
}
