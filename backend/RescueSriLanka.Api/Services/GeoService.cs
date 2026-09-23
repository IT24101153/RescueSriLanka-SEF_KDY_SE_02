namespace RescueSriLanka.Api.Services;

/// <summary>
/// Distance helpers for the "what's near me" queries. Uses the haversine
/// formula on a spherical earth — accurate to well under a percent at the
/// distances this platform cares about, and needs no PostGIS extension.
/// </summary>
public static class GeoService
{
    private const double EarthRadiusKm = 6371.0088;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>
    /// Cheap bounding box around a point, used to narrow the SQL query before
    /// the exact haversine distance is applied in memory.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon) BoundingBox(
        double latitude, double longitude, double radiusKm)
    {
        var latDelta = radiusKm / 111.0;

        // Longitude degrees shrink towards the poles; guard against cos -> 0.
        var cos = Math.Cos(ToRadians(latitude));
        var lonDelta = radiusKm / (111.320 * Math.Max(Math.Abs(cos), 0.000001));

        return (latitude - latDelta, latitude + latDelta,
                longitude - lonDelta, longitude + lonDelta);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
