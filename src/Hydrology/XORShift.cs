// <copyright file="XORShift.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Statistics
{
    using System.Linq;

    /// <summary>
    /// XORShift random number generator.
    /// </summary>
    public class XORShift
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="XORShift"/> class.
        /// </summary>
        /// <param name="seed">RNG seed. Cannot be zero.</param>
        public XORShift(ulong seed)
        {
            if (seed <= 0)
            {
                throw new ArgumentException("Random seed must be greater than zero");
            }

            this.State = seed;
        }

        /// <summary>
        /// Gets or sets current state of the generator.
        /// </summary>
        public ulong State { get; set; }

        /// <summary>
        /// Get next random number in sequence.
        /// </summary>
        /// <returns>Next random number in sequence.</returns>
        public double NextDouble()
        {
            ulong x = this.State;
            x ^= x >> 12; // a
            x ^= x << 25; // b
            x ^= x >> 27; // c
            this.State = x;
            return (x * 0x2545F4914F6CDD1D) / (double)ulong.MaxValue;
        }

        /// <summary>
        /// Returns a uniformly distributed random variable based on the given count. E.g. A count of 5 returns values from 0-4 with equal chance.
        /// </summary>
        /// <param name="count">Possible choices of value.</param>
        /// <returns>Next random number in sequence.</returns>
        public int NextInt(int count)
        {
            int output = (int)Math.Floor(this.NextDouble() * count);

            // Vanishingly small chance of the RNG double equalling 1 - just assign to previous value
            return output == count ? output - 1 : output;
        }

        /// <summary>
        /// Returns a random index from a <see cref="IEnumerable{double}"/> of given weights. Assumes weights sum to 1.
        /// </summary>
        /// <param name="weights">Weights (sum to 1).</param>
        /// <returns>Random index.</returns>
        public int WeightedChoice(IEnumerable<double> weights)
        {
            double random = this.NextDouble();

            // Loop over weights until random number is exceeded
            double sum = 0;
            int selected = 0;
            foreach (double weight in weights)
            {
                sum += weight;
                if (sum >= random)
                {
                    break;
                }

                selected++;
            }

            return selected;
        }

        /// <inheritdoc cref="WeightedChoice(IEnumerable{double})"/>
        /// <param name="weights">Weights (sum to 1).</param>
        public string WeightedChoice(Dictionary<string, double> weights)
        {
            int index = this.WeightedChoice(weights.Values);
            return weights.Keys.ElementAt(index);
        }

        /// <summary>
        /// Returns a selected member from a dictionary with uniform chance.
        /// </summary>
        /// <typeparam name="T">Type of dictionary.</typeparam>
        /// <returns>Key.</returns>
        /// <param name="choices">Choices.</param>
        public string UniformChoice<T>(Dictionary<string, T> choices)
        {
            int selectedIndex = this.NextInt(choices.Count);
            KeyValuePair<string, T> selectedMember = choices.ElementAt(selectedIndex);
            return selectedMember.Key;
        }

        /// <summary>
        /// Returns a selected member from a list with uniform chance.
        /// </summary>
        /// <typeparam name="T">Type of list.</typeparam>
        /// <returns>Key.</returns>
        /// <param name="choices">Choices.</param>
        public T UniformChoice<T>(List<T> choices)
        {
            int selectedIndex = this.NextInt(choices.Count);
            return choices[selectedIndex];
        }

        /// <summary>
        /// Returns a number of selected choices from a list with uniform chance without replacement.
        /// </summary>
        /// <typeparam name="T">Type of list.</typeparam>
        /// <param name="choices">Choices.</param>
        /// <param name="amountToDraw">Number of choices to draw.</param>
        /// <returns>Unique choices.</returns>
        public List<T> ChoiceWithoutReplacement<T>(List<T> choices, int amountToDraw)
        {
            int choiceCount = choices.Count;
            List<int> indices = Enumerable.Range(0, choiceCount).ToList();
            List<T> output = new List<T>();
            int selectedIndex;
            for (int i = 0; i < amountToDraw; i++)
            {
                selectedIndex = this.NextInt(choiceCount);
                output.Add(choices[selectedIndex]);
                indices.RemoveAt(selectedIndex);
                choiceCount--;
            }

            return output;
        }
    }
}
