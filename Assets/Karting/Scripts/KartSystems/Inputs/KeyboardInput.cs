using UnityEngine;

namespace KartGame.KartSystems
{
    public class KeyboardInput : MonoBehaviour, IInput
    {
        public KeyCode accelerateKey = KeyCode.Z;
        public KeyCode brakeKey = KeyCode.S;
        public KeyCode turnLeftKey = KeyCode.Q;
        public KeyCode turnRightKey = KeyCode.D;
        public KeyCode jumpKey = KeyCode.Space;

        public InputData GenerateInput()
        {
            return new InputData
            {
                Accelerate = Input.GetKey(accelerateKey) ? 1f : 0f,
                Brake = Input.GetKey(brakeKey) ? 1f : 0f,
                TurnInput = (Input.GetKey(turnRightKey) ? 1f : 0f) - (Input.GetKey(turnLeftKey) ? 1f : 0f),
                Jump = Input.GetKeyDown(jumpKey), // FIX: Impulsion unique avec GetKeyDown
                Boost = false
            };
        }
    }
}