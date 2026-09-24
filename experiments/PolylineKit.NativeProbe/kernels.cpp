#include <cmath>
#include <cstdint>

struct Point { double x, y; };
struct Edge { Point a, b, origin; };
struct Box { double minX, maxX, minY, maxY; };

static __forceinline double tail(double x, double y, double difference) {
    const double yv = x - difference, xv = difference + yv;
    return (x - xv) + (yv - y);
}
static __forceinline double cross(const Edge& e) {
    const double ax = e.a.x - e.origin.x, bx = e.b.x - e.origin.x;
    const double ay = e.a.y - e.origin.y, by = e.b.y - e.origin.y;
    const double dx = (ax + bx) + (tail(e.a.x, e.origin.x, ax) + tail(e.b.x, e.origin.x, bx));
    const double dy = (ay + by) + (tail(e.a.y, e.origin.y, ay) + tail(e.b.y, e.origin.y, by));
    return dx * (e.b.y - e.a.y) - dy * (e.b.x - e.a.x);
}

// Same operation order as WindingEngine.Cross and compensated Sum.Add, no FMA/reassociation.
extern "C" __declspec(dllexport) __declspec(noinline) double edge_sum(const Edge* edges, int n) {
    double sum = 0, error = 0;
    for (int i = 0; i < n; ++i) {
        const double term = cross(edges[i]), next = sum + term;
        error += std::abs(sum) >= std::abs(term) ? (sum - next) + term : (term - next) + sum;
        sum = next;
    }
    return sum + error;
}

// Only broad-phase candidate enumeration on sorted boxes; not complete geometry.
extern "C" __declspec(dllexport) __declspec(noinline) std::int64_t box_pairs(const Box* boxes, int n) {
    std::int64_t pairs = 0;
    for (int i = 0; i < n; ++i) {
        const Box a = boxes[i];
        for (int j = i + 1; j < n && boxes[j].minX <= a.maxX; ++j) {
            if (boxes[j].maxY < a.minY || boxes[j].minY > a.maxY) continue;
            ++pairs;
        }
    }
    return pairs;
}
