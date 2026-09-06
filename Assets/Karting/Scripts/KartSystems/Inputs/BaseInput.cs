using UnityEngine;

namespace KartGame.KartSystems
{
    public struct InputData
    {
        public float Accelerate;
        public float Brake;
        public float TurnInput;
        public bool Jump;
        public bool Boost;
    }

    public interface IInput
    {
        InputData GenerateInput();
    }

    public abstract class BaseInput : MonoBehaviour, IInput
    {
        /// <summary>
        /// Override this function to generate an XY input that can be used to steer and control the car.
        /// </summary>
        public abstract InputData GenerateInput();
    }
}
