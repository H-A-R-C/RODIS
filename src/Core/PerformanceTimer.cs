// <copyright file="PerformanceTimer.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    /// <summary>
    /// Lightweight named-section timer for diagnosing performance bottlenecks.
    /// Call <see cref="Start"/> and <see cref="Stop"/> around sections of interest.
    /// Call <see cref="Report"/> at the end to write a summary to the console.
    /// </summary>
    public class PerformanceTimer
    {
        private readonly Dictionary<string, Stopwatch> watches = new();
        private readonly Dictionary<string, int> counts = new();
        private readonly List<string> insertionOrder = new();

        /// <summary>Starts (or resumes) timing for the named section.</summary>
        public void Start(string sectionName)
        {
            if (!watches.ContainsKey(sectionName))
            {
                watches[sectionName] = new Stopwatch();
                counts[sectionName] = 0;
                insertionOrder.Add(sectionName);
            }

            watches[sectionName].Start();
            counts[sectionName]++;
        }

        /// <summary>Stops timing for the named section.</summary>
        public void Stop(string sectionName)
        {
            if (watches.ContainsKey(sectionName))
                watches[sectionName].Stop();
        }

        /// <summary>Writes a summary table of all timed sections to the console, sorted by insertion order.</summary>
        public void Report()
        {
            Console.WriteLine();
            Console.WriteLine("══ Performance Summary ══════════════════════════════════════════════");
            Console.WriteLine($"  {"Section",-45} {"Calls",6} {"Total (s)",10} {"Mean (ms)",10} {"% Total",8}");
            Console.WriteLine($"  {new string('-', 45)} {new string('-', 6)} {new string('-', 10)} {new string('-', 10)} {new string('-', 8)}");

            double grandTotal = watches.Values.Sum(w => w.Elapsed.TotalSeconds);
            if (grandTotal < 1e-9)
                grandTotal = 1e-9;

            foreach (string name in insertionOrder)
            {
                double totalSec = watches[name].Elapsed.TotalSeconds;
                int callCount = counts[name];
                double meanMs = callCount > 0 ? (totalSec / callCount) * 1000.0 : 0.0;
                double pct = (totalSec / grandTotal) * 100.0;

                Console.WriteLine($"  {name,-45} {callCount,6} {totalSec,10:F3} {meanMs,10:F1} {pct,7:F1}%");
            }

            Console.WriteLine($"  {new string('═', 83)}");
            Console.WriteLine($"  {"TOTAL",-45} {"",6} {grandTotal,10:F3}");
            Console.WriteLine();
        }

        /// <summary>Resets all timers and counts.</summary>
        public void Reset()
        {
            watches.Clear();
            counts.Clear();
            insertionOrder.Clear();
        }
    }
}