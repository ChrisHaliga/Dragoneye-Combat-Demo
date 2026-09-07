using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Sim
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// A <see cref="CombatEvent"/> as a flat run of integers, and back.
    ///
    /// One array rather than a struct with a dozen fields, because what crosses the wire is one
    /// message and the transport's job is to carry it, not to know its shape. Pure, so the round
    /// trip is checked in the harness against every kind of event the fight can send.
    /// </summary>
    public static class CombatEventCodec
    {
        // The fixed head, then five counted lists.
        const int Head = 13;

        public static int[] Pack(CombatEvent e)
        {
            var ints = new List<int>(Head + 32)
            {
                (int)e.Kind, e.Round, unchecked((int)e.Actor), unchecked((int)e.Target), e.Skill,
                e.Amount, e.Absorbed, e.Hp, e.Armour, e.ApUnits, e.Facing, e.Landed ? 1 : 0,
                (int)e.Outcome
            };

            Elements(ints, e.Elements);
            Elements(ints, e.Answer);
            Elements(ints, e.Returned);

            ints.Add(e.Path.Count);

            foreach (var cell in e.Path)
            {
                ints.Add(cell.Tile.Q);
                ints.Add(cell.Tile.R);
                ints.Add(cell.Area);
            }

            ints.Add(e.Order.Count);

            foreach (var id in e.Order)
            {
                ints.Add(unchecked((int)id));
            }

            ints.Add(e.Starts.Count);

            foreach (var start in e.Starts)
            {
                ints.Add(unchecked((int)start.Id));
                ints.Add(start.Cell.Tile.Q);
                ints.Add(start.Cell.Tile.R);
                ints.Add(start.Cell.Area);
                ints.Add(start.Facing);
                ints.Add(start.Hp);
                ints.Add(start.Armour);
                ints.Add(start.ApUnits);
            }

            ints.Add(e.Segment.Tile.Q);
            ints.Add(e.Segment.Tile.R);
            ints.Add(e.Segment.Index);
            ints.Add(e.Segment.IsRay ? 1 : 0);
            ints.Add((int)e.WallBefore);
            ints.Add((int)e.WallAfter);

            return ints.ToArray();
        }

        /// <summary>The event, or null for a run of integers that is not one.</summary>
        public static CombatEvent Unpack(int[] ints)
        {
            if (ints == null || ints.Length < Head)
            {
                return null;
            }

            var e = new CombatEvent
            {
                Kind = (CombatEventKind)ints[0],
                Round = ints[1],
                Actor = unchecked((uint)ints[2]),
                Target = unchecked((uint)ints[3]),
                Skill = ints[4],
                Amount = ints[5],
                Absorbed = ints[6],
                Hp = ints[7],
                Armour = ints[8],
                ApUnits = ints[9],
                Facing = ints[10],
                Landed = ints[11] != 0,
                Outcome = (ClashOutcome)ints[12]
            };

            var at = Head;

            if (!TryElements(ints, ref at, out var elements)
                || !TryElements(ints, ref at, out var answer)
                || !TryElements(ints, ref at, out var returned)
                || !TryCount(ints, ref at, 3, out var pathCount))
            {
                return null;
            }

            e.Elements = elements;
            e.Answer = answer;
            e.Returned = returned;

            var path = new List<Cell>(pathCount);

            for (var i = 0; i < pathCount; i++)
            {
                path.Add(new Cell(new Hex(ints[at], ints[at + 1]), (byte)ints[at + 2]));
                at += 3;
            }

            e.Path = path;

            if (!TryCount(ints, ref at, 1, out var orderCount))
            {
                return null;
            }

            var order = new List<uint>(orderCount);

            for (var i = 0; i < orderCount; i++)
            {
                order.Add(unchecked((uint)ints[at++]));
            }

            e.Order = order;

            if (!TryCount(ints, ref at, 8, out var startCount))
            {
                return null;
            }

            var starts = new List<CreatureStart>(startCount);

            for (var i = 0; i < startCount; i++)
            {
                starts.Add(new CreatureStart(unchecked((uint)ints[at]),
                    new Cell(new Hex(ints[at + 1], ints[at + 2]), (byte)ints[at + 3]),
                    ints[at + 4], ints[at + 5], ints[at + 6], ints[at + 7]));
                at += 8;
            }

            e.Starts = starts;

            if (at + 6 > ints.Length)
            {
                return null;
            }

            var tile = new Hex(ints[at], ints[at + 1]);
            e.Segment = ints[at + 3] != 0
                ? WallSegment.Ray(tile, ints[at + 2])
                : WallSegment.HalfEdge(tile, ints[at + 2]);
            e.WallBefore = (WallFlags)ints[at + 4];
            e.WallAfter = (WallFlags)ints[at + 5];

            return e;
        }

        static void Elements(List<int> into, IReadOnlyList<Element> elements)
        {
            into.Add(elements.Count);

            foreach (var element in elements)
            {
                into.Add((int)element);
            }
        }

        static bool TryElements(int[] ints, ref int at, out IReadOnlyList<Element> elements)
        {
            elements = Array.Empty<Element>();

            if (!TryCount(ints, ref at, 1, out var count))
            {
                return false;
            }

            var list = new List<Element>(count);

            for (var i = 0; i < count; i++)
            {
                var element = (Element)ints[at++];

                // An element arrives as a number, and casting to an enum is not a checked conversion.
                if (ElementInfo.IsDefined(element))
                {
                    list.Add(element);
                }
            }

            elements = list;
            return true;
        }

        /// <summary>A count, checked against what is left so a short message cannot read past its end.</summary>
        static bool TryCount(int[] ints, ref int at, int stride, out int count)
        {
            count = 0;

            if (at >= ints.Length)
            {
                return false;
            }

            count = ints[at++];

            if (count < 0 || at + (long)count * stride > ints.Length)
            {
                count = 0;
                return false;
            }

            return true;
        }
    }
}
