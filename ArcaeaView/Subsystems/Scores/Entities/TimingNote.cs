namespace Moe.Mottomo.ArcaeaSim.Subsystems.Scores.Entities {
    /// <inheritdoc cref="NoteBase" />
    /// <inheritdoc cref="IHasTick"/>
    /// <summary>
    /// Represents a timing note.
    /// </summary>
    public sealed class TimingNote : NoteBase, IHasTick {

        public int Tick { get; set; }

        /// <summary>
        /// New tempo value.
        /// </summary>
        public float Bpm { get; set; }

        /// <summary>
        /// Beats per measure.
        /// </summary>
        public float BeatsPerMeasure { get; set; }

        public override NoteType Type => NoteType.Timing;

    }
}
