/*
 * IGestureListener - interface for receiving skeleton and gesture data (Lab 3 socket pattern)
 * References: Lab 3 Sockets, 4.3 gesture recognition
 */

using System.Collections.Generic;

namespace GestureClient
{
    public interface IGestureListener
    {
        void OnSkeletonUpdate(double timestamp, IList<SkeletonLandmark> landmarks);
        void OnGestureRecognized(double timestamp, RecognizedGesture gesture);
    }

    public class SkeletonLandmark
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Visibility { get; set; }
    }

    public class RecognizedGesture
    {
        public string Name { get; set; }
        public double Confidence { get; set; }
    }
}
