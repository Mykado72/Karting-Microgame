using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.VFX;

namespace KartGame.KartSystems
{
    /// <summary>
    /// ARCADE KART COMPLETE SYSTEM v2.0
    /// Features:
    /// - Jump mechanic with auto-drift on landing
    /// - Advanced drift with power sliding
    /// - Boost/Nitro system with consumption
    /// - Combo system with score tracking
    /// - Drift chaining (jump into existing drift)
    /// - VFX pool management
    /// 
    /// Author: Optimized for Unity 2020.3+
    /// </summary>
    public class ArcadeKart : MonoBehaviour
    {
        [System.Serializable]
        public class StatPowerup
        {
            public ArcadeKart.Stats modifiers;
            public string PowerUpID;
            public float ElapsedTime;
            public float MaxTime;
        }

        [System.Serializable]
        public struct Stats
        {
            [Header("Movement Settings")]
            [Min(0.001f)] public float TopSpeed;
            [Min(0.001f)] public float Acceleration;
            [Min(0.001f)] public float ReverseSpeed;
            [Min(0.001f)] public float ReverseAcceleration;
            [Range(0.2f, 1)] public float AccelerationCurve;
            [Min(0.001f)] public float Braking;
            [Min(0.001f)] public float CoastingDrag;
            [Range(0.0f, 2.0f)] public float Grip;
            [Min(0.001f)] public float Steer;
            [Min(0.001f)] public float AddedGravity;

            public static Stats operator +(Stats a, Stats b)
            {
                return new Stats
                {
                    Acceleration        = a.Acceleration + b.Acceleration,
                    AccelerationCurve   = a.AccelerationCurve + b.AccelerationCurve,
                    Braking             = a.Braking + b.Braking,
                    CoastingDrag        = a.CoastingDrag + b.CoastingDrag,
                    AddedGravity        = a.AddedGravity + b.AddedGravity,
                    Grip                = a.Grip + b.Grip,
                    ReverseAcceleration = a.ReverseAcceleration + b.ReverseAcceleration,
                    ReverseSpeed        = a.ReverseSpeed + b.ReverseSpeed,
                    TopSpeed            = a.TopSpeed + b.TopSpeed,
                    Steer               = a.Steer + b.Steer,
                };
            }
        }

        // ========== COMBO SYSTEM ==========
        [System.Serializable]
        public class DriftCombo
        {
            [Min(1)] public int ComboMultiplier = 1;
            [Min(0)] public float ComboTime = 0f;
            [Min(0.1f)] public float ComboMaxTime = 2f;  // Réinitialiser combo après 2s sans drift
            [Min(1)] public int BaseComboPoints = 10;
            [Min(0.5f)] public float ComboDecayRate = 0.95f;  // Multiplicateur décaie avec temps
            
            public int TotalPoints { get; private set; } = 0;
            public int CurrentComboMultiplier { get; private set; } = 1;
            public bool IsActive { get; private set; } = false;

            public void StartCombo() => IsActive = true;
            public void EndCombo() => IsActive = false;
            public void ResetCombo()
            {
                IsActive = false;
                ComboMultiplier = 1;
                ComboTime = 0f;
                CurrentComboMultiplier = 1;
            }

            public void UpdateCombo(float deltaTime)
            {
                if (!IsActive) return;
                
                ComboTime += deltaTime;
                if (ComboTime > ComboMaxTime)
                {
                    ResetCombo();
                }
            }

            public void AddDriftPoints(float driftDuration)
            {
                if (!IsActive) return;
                
                // Points basés sur durée drift et multiplicateur
                int points = Mathf.Max(1, (int)(BaseComboPoints * driftDuration * CurrentComboMultiplier));
                TotalPoints += points;
                ComboMultiplier++;
                CurrentComboMultiplier = ComboMultiplier;
            }
        }

        // ========== RAYCAST CONSTANTS ==========
        private static class RaycastConfig
        {
            public const int LayerMask = (1 << 9) | (1 << 10) | (1 << 11);
            public const float Distance = 3.0f;
            public const float HeightOffset = 0.1f;
        }

        // ========== PHYSICS CONSTANTS ==========
        private const float k_NullInput = 0.01f;
        private const float k_NullSpeed = 0.01f;
        private const float k_VectorMagnitudeTolerance = 0.001f;
        private const float k_AngularVelocityDamping = 0.98f;
        private const float k_GroundThresholdForAirborneControl = 0.7f;
        private const float k_GroundThresholdForLanding = 0.1f;
        private const float k_AccelerationCurveCoefficient = 5f;
        private const float k_GroundDriftBoostMultiplier = 10f;

        // ========== PUBLIC PROPERTIES ==========
        [Header("Drift Settings")]
        public float DriftAngularVelocitySteering = 1.25f;
        public float DriftAngularVelocitySmoothSpeed = 4f;

        [Header("Advanced Steering")]
        [Range(1f, 2f)] public float VelocitySteeringCoefficient = 1f;

        [Header("Jump & Boost Drift")]
        [Min(0.1f)] public float JumpForce = 10f;
        [Range(0f, 1f)] public float MinSpeedPercentForJump = 0.3f;
        public bool ForceJumpDriftEntry = true;
        [Min(0.1f)] public float JumpCooldown = 0.5f;

        [Header("Boost/Nitro System")]
        [Min(0.1f)] public float MaxBoostEnergy = 100f;
        [Min(0.1f)] public float BoostConsumptionRate = 20f;  // Par seconde
        [Min(0.1f)] public float BoostRegenRate = 10f;  // Par seconde
        [Range(1f, 3f)] public float BoostSpeedMultiplier = 1.5f;  // Speed × 1.5
        [Range(0.5f, 2f)] public float BoostAccelMultiplier = 1.3f;
        public bool CanBoostDuringDrift = true;

        [Header("Drift Combo System")]
        public DriftCombo DriftComboSystem = new DriftCombo();

        [Header("Drift Chaining")]
        public bool AllowDriftChaining = true;
        [Range(0.1f, 1f)] public float DriftChainAngleThreshold = 0.5f;  // Cosine threshold
        [Range(1f, 3f)] public float DriftChainGripBonus = 1.2f;  // 20% meilleure grip en chain

        public Rigidbody Rigidbody { get; private set; }
        public InputData Input     { get; private set; }
        public float AirPercent    { get; private set; }
        public float GroundPercent { get; private set; }
        public bool JumpLandedThisFrame { get; private set; } = false;
        public float CurrentBoostEnergy { get; private set; }
        public bool IsBoostActive { get; private set; } = false;

        public ArcadeKart.Stats baseStats = new ArcadeKart.Stats
        {
            TopSpeed            = 10f,
            Acceleration        = 5f,
            AccelerationCurve   = 4f,
            Braking             = 10f,
            ReverseAcceleration = 5f,
            ReverseSpeed        = 5f,
            Steer               = 5f,
            CoastingDrag        = 4f,
            Grip                = .95f,
            AddedGravity        = 1f,
        };

        [Header("Vehicle Visual")] 
        public List<GameObject> m_VisualWheels;

        [Header("Vehicle Physics")]
        public Transform CenterOfMass;
        [Range(0.0f, 20.0f)] public float AirborneReorientationCoefficient = 3.0f;

        [Header("Drifting")]
        [Range(0.01f, 1.0f)] public float DriftGrip = 0.4f;
        [Range(0.0f, 10.0f)] public float DriftAdditionalSteer = 5.0f;
        [Range(1.0f, 45.0f)] public float MinAngleToFinishDrift = 10.0f;
        [Range(0.01f, 0.99f)] public float MinSpeedPercentToFinishDrift = 0.5f;
        [Range(1.0f, 40.0f)] public float DriftControl = 20.0f;
        [Range(0.0f, 60.0f)] public float DriftDampening = 20.0f;
        [Range(0.0f, 4.0f)] public float MinTurnTimeForDrift = 0.3f;

        [Header("VFX")]
        public ParticleSystem DriftSparkVFX;
        [Range(0.0f, 0.2f)] public float DriftSparkHorizontalOffset = 0.1f;
        [Range(0.0f, 90.0f)] public float DriftSparkRotation = 17.0f;
        public GameObject DriftTrailPrefab;
        [Range(-0.1f, 0.1f)] public float DriftTrailVerticalOffset;
        public GameObject JumpVFX;
        public GameObject JumpDriftVFX;
        public GameObject BoostVFX;
        public GameObject NozzleVFX;
        public List<Transform> Nozzles;

        [Header("Suspensions")]
        [Range(0.0f, 1.0f)] public float SuspensionHeight = 0.2f;
        [Range(10.0f, 100000.0f)] public float SuspensionSpring = 20000.0f;
        [Range(0.0f, 5000.0f)] public float SuspensionDamp = 500.0f;
        [Range(-1.0f, 1.0f)] public float WheelsPositionVerticalOffset = 0.0f;

        [Header("Physical Wheels")]
        public WheelCollider FrontLeftWheel;
        public WheelCollider FrontRightWheel;
        public WheelCollider RearLeftWheel;
        public WheelCollider RearRightWheel;
        public LayerMask GroundLayers = Physics.DefaultRaycastLayers;

        // Internal state
        IInput[] m_Inputs;
        Vector3 m_VerticalReference = Vector3.up;

        // Drift params
        public bool WantsToDrift { get; private set; } = false;
        public bool IsDrifting { get; private set; } = false;
        bool m_IsDriftChaining = false;  // In another drift
        float m_CurrentGrip = 1.0f;
        float m_DriftTurningPower = 0.0f;
        float m_PreviousGroundPercent = 1.0f;
        float m_DriftInputTime = 0.0f;
        float m_DriftDuration = 0f;  // Track how long in drift
        
        // Jump params
        bool m_WasInAirLastFrame = false;
        float m_JumpCooldownTimer = 0f;
        bool m_JumpedThisFrame = false;
        
        // Boost params
        GameObject m_BoostVFXInstance;
        
        readonly List<(GameObject trailRoot, WheelCollider wheel, TrailRenderer trail)> m_DriftTrailInstances = 
            new List<(GameObject, WheelCollider, TrailRenderer)>();
        readonly List<(WheelCollider wheel, float horizontalOffset, float rotation, ParticleSystem sparks)> m_DriftSparkInstances = 
            new List<(WheelCollider, float, float, ParticleSystem)>();

        bool m_CanMove = true;
        List<StatPowerup> m_ActivePowerupList = new List<StatPowerup>();
        ArcadeKart.Stats m_FinalStats;

        Quaternion m_LastValidRotation;
        Vector3 m_LastValidPosition;
        Vector3 m_LastCollisionNormal;
        bool m_HasCollision;
        bool m_InAir = false;

        public void AddPowerup(StatPowerup statPowerup) => m_ActivePowerupList.Add(statPowerup);
        public void SetCanMove(bool move) => m_CanMove = move;
        public float GetMaxSpeed() => Mathf.Max(m_FinalStats.TopSpeed, m_FinalStats.ReverseSpeed);

        private void ActivateDriftVFX(bool active)
        {
            foreach (var vfx in m_DriftSparkInstances)
            {
                if (active && vfx.wheel.GetGroundHit(out WheelHit hit))
                {
                    if (!vfx.sparks.isPlaying)
                        vfx.sparks.Play();
                }
                else
                {
                    if (vfx.sparks.isPlaying)
                        vfx.sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            foreach (var trail in m_DriftTrailInstances)
                trail.Item3.emitting = active && trail.wheel.GetGroundHit(out WheelHit hit);
        }

        private void UpdateDriftVFXOrientation()
        {
            foreach (var vfx in m_DriftSparkInstances)
            {
                vfx.sparks.transform.position = vfx.wheel.transform.position - (vfx.wheel.radius * Vector3.up) + 
                                                (DriftTrailVerticalOffset * Vector3.up) + 
                                                (transform.right * vfx.horizontalOffset);
                vfx.sparks.transform.rotation = transform.rotation * Quaternion.Euler(0.0f, 0.0f, vfx.rotation);
            }

            foreach (var trail in m_DriftTrailInstances)
            {
                trail.trailRoot.transform.position = trail.wheel.transform.position - (trail.wheel.radius * Vector3.up) + 
                                                     (DriftTrailVerticalOffset * Vector3.up);
                trail.trailRoot.transform.rotation = transform.rotation;
            }
        }

        void UpdateSuspensionParams(WheelCollider wheel)
        {
            wheel.suspensionDistance = SuspensionHeight;
            wheel.center = new Vector3(0.0f, WheelsPositionVerticalOffset, 0.0f);
            JointSpring spring = wheel.suspensionSpring;
            spring.spring = SuspensionSpring;
            spring.damper = SuspensionDamp;
            wheel.suspensionSpring = spring;
        }

        void Awake()
        {
            Rigidbody = GetComponent<Rigidbody>();
            m_Inputs = GetComponents<IInput>();
            CurrentBoostEnergy = MaxBoostEnergy;

            UpdateSuspensionParams(FrontLeftWheel);
            UpdateSuspensionParams(FrontRightWheel);
            UpdateSuspensionParams(RearLeftWheel);
            UpdateSuspensionParams(RearRightWheel);

            m_CurrentGrip = baseStats.Grip;

            if (DriftSparkVFX != null)
            {
                AddSparkToWheel(RearLeftWheel, -DriftSparkHorizontalOffset, -DriftSparkRotation);
                AddSparkToWheel(RearRightWheel, DriftSparkHorizontalOffset, DriftSparkRotation);
            }

            if (DriftTrailPrefab != null)
            {
                AddTrailToWheel(RearLeftWheel);
                AddTrailToWheel(RearRightWheel);
            }

            if (NozzleVFX != null)
            {
                foreach (var nozzle in Nozzles)
                {
                    Instantiate(NozzleVFX, nozzle, false);
                }
            }
        }

        void AddTrailToWheel(WheelCollider wheel)
        {
            GameObject trailRoot = Instantiate(DriftTrailPrefab, gameObject.transform, false);
            TrailRenderer trail = trailRoot.GetComponentInChildren<TrailRenderer>();
            trail.emitting = false;
            m_DriftTrailInstances.Add((trailRoot, wheel, trail));
        }

        void AddSparkToWheel(WheelCollider wheel, float horizontalOffset, float rotation)
        {
            GameObject vfx = Instantiate(DriftSparkVFX.gameObject, wheel.transform, false);
            ParticleSystem spark = vfx.GetComponent<ParticleSystem>();
            spark.Stop();
            m_DriftSparkInstances.Add((wheel, horizontalOffset, -rotation, spark));
        }

        void FixedUpdate()
        {
            GatherInputs();
            TickPowerups();
            Rigidbody.centerOfMass = transform.InverseTransformPoint(CenterOfMass.position);

            int groundedCount = 0;
            if (FrontLeftWheel.isGrounded && FrontLeftWheel.GetGroundHit(out WheelHit hit)) groundedCount++;
            if (FrontRightWheel.isGrounded && FrontRightWheel.GetGroundHit(out hit)) groundedCount++;
            if (RearLeftWheel.isGrounded && RearLeftWheel.GetGroundHit(out hit)) groundedCount++;
            if (RearRightWheel.isGrounded && RearRightWheel.GetGroundHit(out hit)) groundedCount++;

            GroundPercent = (float)groundedCount / 4.0f;
            AirPercent = 1 - GroundPercent;

            if (m_CanMove)
            {
                MoveVehicle(Input.Accelerate > 0.0f, Input.Brake > 0.0f, Input.TurnInput);
            }

            // UpdateBoostSystem(Input.Boost);
            // UpdateComboSystem();
            
            m_JumpCooldownTimer -= Time.fixedDeltaTime;
            m_PreviousGroundPercent = GroundPercent;
            JumpLandedThisFrame = false;

            UpdateDriftVFXOrientation();
        }

        void GatherInputs()
        {
            InputData combinedInput = new InputData();
            WantsToDrift = false;

            for (int i = 0; i < m_Inputs.Length; i++)
            {
                InputData current = m_Inputs[i].GenerateInput();

                // On conserve la valeur la plus forte pour ne pas écraser les commandes actives
                combinedInput.Accelerate = Mathf.Max(combinedInput.Accelerate, current.Accelerate);
                combinedInput.Brake = Mathf.Max(combinedInput.Brake, current.Brake);

                if (Mathf.Abs(current.TurnInput) > Mathf.Abs(combinedInput.TurnInput))
                    combinedInput.TurnInput = current.TurnInput;

                combinedInput.Jump |= current.Jump;
                combinedInput.Boost |= current.Boost;
            }

            Input = combinedInput;

            if (Input.Brake>0.0 && Vector3.Dot(Rigidbody.velocity, transform.forward) > 0.0f)
            {
                WantsToDrift = true;
            }
        }
        

        void TickPowerups()
        {
            m_ActivePowerupList.RemoveAll((p) => { return p.ElapsedTime > p.MaxTime; });

            var powerups = new Stats();

            for (int i = 0; i < m_ActivePowerupList.Count; i++)
            {
                var p = m_ActivePowerupList[i];
                p.ElapsedTime += Time.fixedDeltaTime;
                powerups += p.modifiers;
            }

            m_FinalStats = baseStats + powerups;
            m_FinalStats.Grip = Mathf.Clamp(m_FinalStats.Grip, 0, 1);
        }

        void UpdateBoostSystem(bool boostInput)
        {
            // Boost activation
            if (boostInput && CurrentBoostEnergy > 5f && GroundPercent > 0.3f && 
                (CanBoostDuringDrift || !IsDrifting))
            {
                IsBoostActive = true;
                CurrentBoostEnergy -= BoostConsumptionRate * Time.fixedDeltaTime;

                if (m_BoostVFXInstance == null && BoostVFX != null)
                {
                    m_BoostVFXInstance = Instantiate(BoostVFX, transform);
                }
            }
            else
            {
                IsBoostActive = false;
                if (m_BoostVFXInstance != null)
                {
                    Destroy(m_BoostVFXInstance);
                    m_BoostVFXInstance = null;
                }
            }

            // Boost regen
            if (!IsBoostActive && CurrentBoostEnergy < MaxBoostEnergy)
            {
                CurrentBoostEnergy += BoostRegenRate * Time.fixedDeltaTime;
            }

            CurrentBoostEnergy = Mathf.Clamp(CurrentBoostEnergy, 0, MaxBoostEnergy);
        }

        void UpdateComboSystem()
        {
            if (IsDrifting)
            {
                if (!DriftComboSystem.IsActive)
                {
                    DriftComboSystem.StartCombo();
                }
                m_DriftDuration += Time.fixedDeltaTime;
            }
            else
            {
                if (DriftComboSystem.IsActive)
                {
                    DriftComboSystem.AddDriftPoints(m_DriftDuration);
                    m_DriftDuration = 0f;
                }
                DriftComboSystem.EndCombo();
            }

            DriftComboSystem.UpdateCombo(Time.fixedDeltaTime);
        }

        public void Reset()
        {
            Vector3 euler = transform.rotation.eulerAngles;
            euler.x = euler.z = 0f;
            transform.rotation = Quaternion.Euler(euler);
        }

        public float LocalSpeed()
        {
            if (m_CanMove)
            {
                float dot = Vector3.Dot(transform.forward, Rigidbody.velocity);
                if (Mathf.Abs(dot) > 0.1f)
                {
                    float speed = Rigidbody.velocity.magnitude;
                    return dot < 0 ? -(speed / m_FinalStats.ReverseSpeed) : (speed / m_FinalStats.TopSpeed);
                }
                return 0f;
            }
            else
            {
                return Input.Accelerate > 0.0f ? 1.0f : 0.0f;
            }
        }

        void OnCollisionEnter(Collision collision) => m_HasCollision = true;
        void OnCollisionExit(Collision collision) => m_HasCollision = false;

        void OnCollisionStay(Collision collision)
        {
            m_HasCollision = true;
            m_LastCollisionNormal = Vector3.zero;
            float dot = -1.0f;

            foreach (var contact in collision.contacts)
            {
                if (Vector3.Dot(contact.normal, Vector3.up) > dot)
                    m_LastCollisionNormal = contact.normal;
            }
        }

        void MoveVehicle(bool accelerate, bool brake, float turnInput)
        {
            float accelInput = (accelerate ? 1.0f : 0.0f) - (brake ? 1.0f : 0.0f);
            Vector3 localVel = transform.InverseTransformVector(Rigidbody.velocity);

            bool accelDirectionIsFwd = accelInput >= 0;
            bool localVelDirectionIsFwd = localVel.z >= 0;

            float maxSpeed = localVelDirectionIsFwd ? m_FinalStats.TopSpeed : m_FinalStats.ReverseSpeed;
            float accelPower = accelDirectionIsFwd ? m_FinalStats.Acceleration : m_FinalStats.ReverseAcceleration;

            float currentSpeed = Rigidbody.velocity.magnitude;
            
            // Apply boost multiplier
            if (IsBoostActive)
            {
                maxSpeed *= BoostSpeedMultiplier;
                accelPower *= BoostAccelMultiplier;
            }

            float accelRampT = currentSpeed / maxSpeed;
            float multipliedAccelerationCurve = m_FinalStats.AccelerationCurve * k_AccelerationCurveCoefficient;
            float accelRamp = Mathf.Lerp(multipliedAccelerationCurve, 1, accelRampT * accelRampT);

            bool isBraking = (localVelDirectionIsFwd && brake) || (!localVelDirectionIsFwd && accelerate);
            float finalAccelPower = isBraking ? m_FinalStats.Braking : accelPower;
            float finalAcceleration = finalAccelPower * accelRamp;

            float turningPower = IsDrifting ? m_DriftTurningPower : turnInput * m_FinalStats.Steer;

            Quaternion turnAngle = Quaternion.AngleAxis(turningPower, transform.up);
            Vector3 fwd = turnAngle * transform.forward;
            Vector3 movement = fwd * accelInput * finalAcceleration * ((m_HasCollision || GroundPercent > 0.0f) ? 1.0f : 0.0f);

            bool wasOverMaxSpeed = currentSpeed >= maxSpeed;

            if (wasOverMaxSpeed && !isBraking) 
                movement *= 0.0f;

            Vector3 newVelocity = Rigidbody.velocity + movement * Time.fixedDeltaTime;
            newVelocity.y = Rigidbody.velocity.y;

            if (GroundPercent > 0.0f && !wasOverMaxSpeed)
            {
                newVelocity = Vector3.ClampMagnitude(newVelocity, maxSpeed);
            }

            if (Mathf.Abs(accelInput) < k_NullInput && GroundPercent > 0.0f)
            {
                newVelocity = Vector3.MoveTowards(newVelocity, new Vector3(0, Rigidbody.velocity.y, 0), 
                                                  Time.fixedDeltaTime * m_FinalStats.CoastingDrag);
            }

            Rigidbody.velocity = newVelocity;
            
            if (GroundPercent > 0.0f)
            {
                if (m_InAir)
                {
                    m_InAir = false;
                    
                    if (m_WasInAirLastFrame)
                    {
                        if (JumpLandedThisFrame && JumpDriftVFX != null)
                            Instantiate(JumpDriftVFX, transform.position, Quaternion.identity);
                        else if (JumpVFX != null)
                            Instantiate(JumpVFX, transform.position, Quaternion.identity);
                    }
                }

                UpdateAngularVelocity(turningPower);
                CheckDriftEntry(turnInput, currentSpeed, maxSpeed, turningPower, accelInput);
                UpdateDriftState(turnInput, currentSpeed, maxSpeed, turningPower, isBraking);

                Rigidbody.velocity = Quaternion.AngleAxis(turningPower * Mathf.Sign(localVel.z) * 
                                                          VelocitySteeringCoefficient * m_CurrentGrip * 
                                                          Time.fixedDeltaTime, transform.up) * Rigidbody.velocity;
            }
            else
            {
                m_InAir = true;
                m_WasInAirLastFrame = true;
            }

            UpdateAirborneReorientation();
        }

        private void HandleJump(float currentSpeed, float maxSpeed)
        {
            if (HasJumpInput() && GroundPercent > 0.8f && m_JumpCooldownTimer <= 0f)
            {
                if (currentSpeed >= maxSpeed * MinSpeedPercentForJump)
                {
                    Vector3 jumpVelocity = Rigidbody.velocity;
                    jumpVelocity.y = JumpForce;
                    Rigidbody.velocity = jumpVelocity;
                    
                    m_JumpedThisFrame = true;
                    m_JumpCooldownTimer = JumpCooldown;
                    m_WasInAirLastFrame = true;

#if UNITY_EDITOR
                    Debug.Log("Jump initiated!");
#endif
                }
            }
        }

        private bool HasJumpInput()
        {
            // FIX : Lecture directe du champ Jump de la struct InputData
            return Input.Jump;
        }

        private void UpdateAngularVelocity(float turningPower)
        {
            var angularVel = Rigidbody.angularVelocity;
            angularVel.y = Mathf.MoveTowards(angularVel.y, turningPower * DriftAngularVelocitySteering, 
                                              Time.fixedDeltaTime * DriftAngularVelocitySmoothSpeed);
            Rigidbody.angularVelocity = angularVel;
        }

        private void CheckDriftEntry(float turnInput, float currentSpeed, float maxSpeed, 
                                     float turningPower, float accelInput)
        {
            if (m_PreviousGroundPercent < k_GroundThresholdForLanding && GroundPercent >= 0.0f)
            {
                // Jump drift
                if (m_JumpedThisFrame && ForceJumpDriftEntry)
                {
                    IsDrifting = true;
                    m_CurrentGrip = DriftGrip;
                    m_DriftTurningPower = turningPower + (Mathf.Sign(turningPower) * DriftAdditionalSteer);
                    m_DriftInputTime = MinTurnTimeForDrift;
                    JumpLandedThisFrame = true;
                    ActivateDriftVFX(true);
                    m_JumpedThisFrame = false;
                    m_IsDriftChaining = false;

#if UNITY_EDITOR
                    Debug.Log("Jump drift triggered!");
#endif
                    return;
                }

                // Normal angle-based drift
                Vector3 flattenVelocity = Vector3.ProjectOnPlane(Rigidbody.velocity, m_VerticalReference);
                
                if (flattenVelocity.sqrMagnitude > k_VectorMagnitudeTolerance)
                {
                    flattenVelocity.Normalize();
                    float dotProduct = Vector3.Dot(flattenVelocity, transform.forward * Mathf.Sign(accelInput));
                    
                    if (dotProduct < Mathf.Cos(MinAngleToFinishDrift * Mathf.Deg2Rad) && 
                        m_DriftInputTime >= MinTurnTimeForDrift)
                    {
                        IsDrifting = true;
                        m_CurrentGrip = DriftGrip;
                        m_DriftTurningPower = 0.0f;
                        m_IsDriftChaining = false;
                    }
                }
            }
        }

        private void UpdateDriftState(float turnInput, float currentSpeed, float maxSpeed, 
                                      float turningPower, bool isBraking)
        {
            float turnInputAbs = Mathf.Abs(turnInput);

            if (!IsDrifting)
            {
                // Check for drift chaining (jump into existing drift)
                HandleDriftChaining(turnInput, currentSpeed, maxSpeed);
                UpdateDriftEntry(turnInputAbs, currentSpeed, maxSpeed, turningPower);
            }
            else
            {
                UpdateDriftPhysics(turnInput, turnInputAbs, currentSpeed, maxSpeed, turningPower, isBraking);
            }
        }

        private void HandleDriftChaining(float turnInput, float currentSpeed, float maxSpeed)
        {
            if (!AllowDriftChaining || !m_JumpedThisFrame || IsDrifting) return;

            float turnInputAbs = Mathf.Abs(turnInput);
            
            // Can chain if turning and speed is high enough
            if (turnInputAbs >= k_NullInput && currentSpeed > maxSpeed * MinSpeedPercentToFinishDrift)
            {
                IsDrifting = true;
                m_CurrentGrip = DriftGrip * DriftChainGripBonus;  // Better grip in chain
                m_DriftTurningPower = turnInput * (m_FinalStats.Steer + DriftAdditionalSteer);
                m_DriftInputTime = MinTurnTimeForDrift;
                m_IsDriftChaining = true;
                ActivateDriftVFX(true);
                m_JumpedThisFrame = false;

#if UNITY_EDITOR
                Debug.Log("Drift chaining activated!");
#endif
            }
        }

        private void UpdateDriftEntry(float turnInputAbs, float currentSpeed, float maxSpeed, float turningPower)
        {
            if (turnInputAbs >= k_NullInput)
            {
                m_DriftInputTime += Time.fixedDeltaTime;
            }
            else
            {
                m_DriftInputTime = 0.0f;
            }

            if (currentSpeed > maxSpeed * MinSpeedPercentToFinishDrift && 
                m_DriftInputTime >= MinTurnTimeForDrift)
            {
                IsDrifting = true;
                m_DriftTurningPower = turningPower + (Mathf.Sign(turningPower) * DriftAdditionalSteer);
                m_CurrentGrip = DriftGrip;
                m_IsDriftChaining = false;
                ActivateDriftVFX(true);

#if UNITY_EDITOR
                Debug.Log("Drift started!");
#endif
            }
        }

        private void UpdateDriftPhysics(float turnInput, float turnInputAbs, float currentSpeed, float maxSpeed,
                                        float turningPower, bool isBraking)
        {
            float driftMaxSteerValue = m_FinalStats.Steer + DriftAdditionalSteer;

            if (turnInputAbs > k_NullInput)
            {
                // FIX : Autorise le contre-braquage ou le resserrement du virage selon turnInput
                m_DriftTurningPower = Mathf.MoveTowards(
                    m_DriftTurningPower,
                    turnInput * driftMaxSteerValue,
                    DriftControl * Time.fixedDeltaTime * 10f
                );
            }
            else
            {
                m_DriftTurningPower = Mathf.MoveTowards(
                    m_DriftTurningPower,
                    0.0f,
                    DriftDampening * Time.fixedDeltaTime
                );
            }

            bool facingVelocity = Vector3.Dot(Rigidbody.velocity.normalized, transform.forward * Mathf.Sign(m_DriftTurningPower))
                                  > Mathf.Cos(MinAngleToFinishDrift * Mathf.Deg2Rad);

            bool canEndDrift = !isBraking && facingVelocity &&
                              (turnInputAbs < k_NullInput || currentSpeed <= maxSpeed * MinSpeedPercentToFinishDrift);

            if (canEndDrift || currentSpeed < k_NullSpeed)
            {
                IsDrifting = false;
                m_CurrentGrip = m_FinalStats.Grip;
                m_DriftInputTime = 0.0f;
                m_IsDriftChaining = false;
            }
        }

        private void UpdateAirborneReorientation()
        {
            bool validPosition = false;
            
            if (Physics.Raycast(transform.position + (transform.up * RaycastConfig.HeightOffset), 
                               -transform.up, out RaycastHit hit, RaycastConfig.Distance, RaycastConfig.LayerMask))
            {
                Vector3 lerpVector = (m_HasCollision && m_LastCollisionNormal.y > hit.normal.y) 
                                    ? m_LastCollisionNormal 
                                    : hit.normal;
                
                float lerpSpeed = AirborneReorientationCoefficient * Time.fixedDeltaTime * 
                                 (GroundPercent > 0.0f ? k_GroundDriftBoostMultiplier : 1.0f);
                m_VerticalReference = Vector3.Slerp(m_VerticalReference, lerpVector, Mathf.Clamp01(lerpSpeed));
            }
            else
            {
                Vector3 lerpVector = (m_HasCollision && m_LastCollisionNormal.y > 0.0f) 
                                    ? m_LastCollisionNormal 
                                    : Vector3.up;
                m_VerticalReference = Vector3.Slerp(m_VerticalReference, lerpVector, 
                                                     Mathf.Clamp01(AirborneReorientationCoefficient * Time.fixedDeltaTime));
            }

            validPosition = GroundPercent > k_GroundThresholdForAirborneControl && !m_HasCollision && 
                           Vector3.Dot(m_VerticalReference, Vector3.up) > 0.9f;

            if (GroundPercent < k_GroundThresholdForAirborneControl)
            {
                Rigidbody.angularVelocity = new Vector3(0.0f, Rigidbody.angularVelocity.y * k_AngularVelocityDamping, 0.0f);
                
                Vector3 finalOrientationDirection = Vector3.ProjectOnPlane(transform.forward, m_VerticalReference);
                
                if (finalOrientationDirection.sqrMagnitude > k_VectorMagnitudeTolerance)
                {
                    finalOrientationDirection.Normalize();
                    Rigidbody.MoveRotation(Quaternion.Lerp(Rigidbody.rotation, 
                                                            Quaternion.LookRotation(finalOrientationDirection, m_VerticalReference), 
                                                            Mathf.Clamp01(AirborneReorientationCoefficient * Time.fixedDeltaTime)));
                }
            }
            else if (validPosition)
            {
                m_LastValidPosition = transform.position;
                m_LastValidRotation.eulerAngles = new Vector3(0.0f, transform.rotation.y, 0.0f);
            }

            ActivateDriftVFX(IsDrifting && GroundPercent > 0.0f);
        }
    }
}
