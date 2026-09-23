using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The distance maths behind "what's near me". Every nearby query and every
/// zone containment check runs through here, so a quiet error would show up as
/// hazards silently missing from a citizen's map.
/// </summary>
public class GeoServiceTests
{
    // Real Sri Lankan coordinates — a wrong constant is easier to spot against
    // a distance somebody can sanity-check on a map.
    private const double ColomboLat = 6.9271;
    private const double ColomboLon = 79.8612;
    private const double KandyLat = 7.2906;
    private const double KandyLon = 80.6337;

    [Fact]
    public void DistanceKm_BetweenColomboAndKandy_IsAboutNinetyFourKm()
    {
        var distance = GeoService.DistanceKm(ColomboLat, ColomboLon, KandyLat, KandyLon);

        // Great-circle distance is ~94 km; allow a kilometre for the spherical
        // approximation rather than pinning a magic number.
        Assert.InRange(distance, 93.0, 95.0);
    }

    [Fact]
    public void DistanceKm_ForTheSamePoint_IsZero()
    {
        Assert.Equal(0, GeoService.DistanceKm(ColomboLat, ColomboLon, ColomboLat, ColomboLon), 6);
    }

    [Fact]
    public void DistanceKm_IsSymmetric()
    {
        var there = GeoService.DistanceKm(ColomboLat, ColomboLon, KandyLat, KandyLon);
        var back = GeoService.DistanceKm(KandyLat, KandyLon, ColomboLat, ColomboLon);

        Assert.Equal(there, back, 9);
    }

    [Fact]
    public void BoundingBox_ContainsEveryPointInsideTheRadius()
    {
        const double radiusKm = 25;
        var (minLat, maxLat, minLon, maxLon) =
            GeoService.BoundingBox(ColomboLat, ColomboLon, radiusKm);

        // The box is the SQL pre-filter: anything it excludes never reaches the
        // exact distance check, so a point inside the radius must be inside it.
        // Walking the compass catches a box that is too tight on any one side.
        foreach (var bearing in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
        {
            var radians = bearing * Math.PI / 180.0;
            var latOffset = radiusKm * 0.95 / 111.0 * Math.Cos(radians);
            var lonOffset = radiusKm * 0.95 /
                (111.320 * Math.Cos(ColomboLat * Math.PI / 180.0)) * Math.Sin(radians);

            var lat = ColomboLat + latOffset;
            var lon = ColomboLon + lonOffset;

            Assert.True(
                lat >= minLat && lat <= maxLat && lon >= minLon && lon <= maxLon,
                $"Point at bearing {bearing}° fell outside the bounding box.");
        }
    }

    [Fact]
    public void BoundingBox_GrowsWithTheRadius()
    {
        var (MinLat, MaxLat, MinLon, MaxLon) = GeoService.BoundingBox(ColomboLat, ColomboLon, 10);
        var large = GeoService.BoundingBox(ColomboLat, ColomboLon, 100);

        Assert.True(large.MaxLat > MaxLat);
        Assert.True(large.MinLat < MinLat);
        Assert.True(large.MaxLon > MaxLon);
        Assert.True(large.MinLon < MinLon);
    }

    [Fact]
    public void BoundingBox_AtThePole_DoesNotDivideByZero()
    {
        // cos(90°) is 0. Sri Lanka will never hit this, but a NaN here would
        // poison the SQL filter rather than failing loudly.
        var (minLat, maxLat, minLon, maxLon) = GeoService.BoundingBox(90, 0, 50);

        Assert.False(double.IsNaN(minLat) || double.IsInfinity(minLat));
        Assert.False(double.IsNaN(maxLat) || double.IsInfinity(maxLat));
        Assert.False(double.IsNaN(minLon) || double.IsInfinity(minLon));
        Assert.False(double.IsNaN(maxLon) || double.IsInfinity(maxLon));
    }
}
