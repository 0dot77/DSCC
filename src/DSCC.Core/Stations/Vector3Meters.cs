namespace DSCC.Core.Stations;

public readonly record struct Vector3Meters(double X, double Y, double Z)
{
    public double HorizontalDistanceTo(Vector3Meters other)
    {
        var dx = X - other.X;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
