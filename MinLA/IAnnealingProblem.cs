using System;
using System.Collections.Generic;
using System.Text;

namespace MinLA
{
    public interface IAnnealingProblem<out TR>
    {
        TR Result { get; }

        double Cost { get; }

        /// <summary>
        /// Generates a possible change to the current state and stores the change without applying it.
        /// </summary>
        /// <returns>The cost of the proposed random move.</returns>
        double MakeRandomMove();

        /// <summary>
        /// Applies the change generated in the last call to <see cref="MakeRandomMove"/>
        /// </summary>
        void KeepLastMove();
    }
}
