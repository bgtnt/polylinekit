using PolylineKit;

Point2[] first = [new(0, 0), new(2, 0)];
Point2[] second = [new(0, 0), new(1, 1), new(2, 0)];
double area = PolylineArea.BetweenGraphs(first, second);
Console.WriteLine($"Area: {area}; mean vertical separation: {area / 2}");
if (area != 1) throw new Exception("Example produced an unexpected result.");
