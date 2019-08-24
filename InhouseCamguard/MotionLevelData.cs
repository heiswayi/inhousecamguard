using System;

namespace InhouseCamguard
{
    public class MotionLevelData
    {
        public double ElapsedTime { get; }

        public double Value { get; }

        public double ThresholdValue { get; }

        public DateTime StartTime { get; }

        public DateTime CurrentTime { get; }

        public int BlobCount { get; }

        public MotionLevelData(double _elapsedTime, double _value, double _thresholdValue, DateTime _startTime, DateTime _currentTime, int _blobCount = 0)
        {
            ElapsedTime = _elapsedTime;
            Value = _value;
            ThresholdValue = _thresholdValue;
            StartTime = _startTime;
            CurrentTime = _currentTime;
            BlobCount = _blobCount;
        }
    }
}