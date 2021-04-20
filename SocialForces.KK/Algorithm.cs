using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Karenia.SocialForces.KK
{
    /// <summary>
    /// The main algorithm of Social Forces model.
    /// </summary>
    public static class Algorithm
    {
        // tau_a
        private static float relaxationTime = 0.5f;

        private static float deltaT_social = 1.27f;
        private static float sigma_social = 2f;
        private static float force_social = 4f;

        private static float deltaT_attract = 1f;
        private static float sigma_attract = 10f;
        private static float force_attract = 3f;

        private static float sigma_obstacle = 0.2f;
        private static float force_obstacle = 20.5f;

        private static float target_slowdown = 2f;

        private static float effectiveAngle = 100f;
        private static float effectiveCoeff = 0.5f;

        private static float fluctuationCoeff = 0.02f;

        public static Forces CalculateAcceleration<T, NeighborEnumerator, AttractorEnumerator>(
            Neighbor<T> self,
            Vector3 targetPosition,
            float desiredSpeed,
            NeighborEnumerator neighbors,
            Func<T, T, float> repulsionCoeff,
            Vector3? border,
            AttractorEnumerator attractors,
            Func<T, T, float> attractionCoeff,
            bool is_final_target,
            bool debug = false
        )
            where NeighborEnumerator : IEnumerable<Neighbor<T>>
            where AttractorEnumerator : IEnumerable<Neighbor<T>>
        {
            var desiredDirection = targetPosition - self.position;
            desiredDirection.y = 0;
            //desiredDirection.Normalize();

            // slow down in quadratic fashion
            const float cbrt_3 = 1.4422496f;
            var pending_distance = (targetPosition - self.position).magnitude;
            if (is_final_target && pending_distance < desiredSpeed * target_slowdown)
            {
                var pending_time = cbrt_3 * Mathf.Pow(pending_distance / desiredSpeed, 0.33333333f);
                desiredSpeed *= Mathf.Pow(pending_time / target_slowdown, 2);
            }

            // Destination term
            var destinationTerm = 1 / relaxationTime * ((desiredSpeed * desiredDirection.normalized) - self.velocity);

            // Other pedestrian repulsion term
            var repulsionTerm = Vector3.zero;
            foreach (var repulsor in neighbors)
            {
                var repulsion = Repulsion(self, repulsor, repulsionCoeff(self.id, repulsor.id) * force_social,
                    sigma_social, deltaT_social);

                var isotrophy = 0.7 + 0.3 * (1 - Mathf.Cos(Vector3.Angle(repulsor.position - self.position, desiredDirection))) / 2;

                repulsionTerm += repulsion;
            }

            // wall term
            var borderRepulsionTerm = Vector3.zero;
            if (border != null)
            {
                if (Vector2.Distance(border.Value, targetPosition) > 0.5f)
                {
                    borderRepulsionTerm = RepulsionFromBorder(self.position, border.Value);
                    if (Vector3.Angle(desiredDirection, border.Value - self.position) > effectiveAngle)
                        borderRepulsionTerm *= effectiveCoeff;
                }
            }

            // attraction term
            var attractionTerm = Vector3.zero;
            foreach (var attractor in attractors)
            {
                var attraction = Repulsion(self, attractor, -attractionCoeff(self.id, attractor.id) * force_attract, sigma_attract, deltaT_attract);

                if (Vector3.Angle(desiredDirection, attractor.position - self.position) > effectiveAngle)
                    attraction *= effectiveCoeff;

                attractionTerm += attraction;
            }

            var fluctuations = UnityEngine.Random.insideUnitCircle * fluctuationCoeff;

            var fluctuations3 = new Vector3(fluctuations.x, 0, fluctuations.y);
            var forces = new Forces()
            {
                destination = destinationTerm,
                attraction = attractionTerm,
                repulsion = repulsionTerm,
                border = borderRepulsionTerm,
                fluctuation = fluctuations3
            };

            //if (debug)
            //{
            //    Plugin.Instance.Logger.LogInfo($"self: {self}");
            //    Plugin.Instance.Logger.LogInfo(forces);
            //}

            return forces;
        }

        public static Vector3 Repulsion<T>(
            Neighbor<T> self, Neighbor<T> neighbor, float coeff, float sigma, float deltaT)
        {
            if (coeff == 0) return Vector3.zero;

            var rel_distance = self.position - neighbor.position;
            var rel_velocity = self.velocity - neighbor.velocity;
            var diff_position = rel_distance - rel_velocity * deltaT;

            var b = CalculateSocialB(rel_distance, rel_velocity);

            var mag_diff_Position = diff_position.magnitude;
            var force = Mathf.Exp(-b / sigma)
                * Vector3.Scale(
                    new Vector3(rel_distance.x + mag_diff_Position, 0, rel_distance.z + mag_diff_Position)
                        / 4 * b,
                    rel_distance.normalized + diff_position.normalized);

            //Plugin.Instance.Logger.LogInfo($"{b} {rel_distance} {force} {coeff}");

            return force * coeff;
        }

        /// <summary>
        /// Calculates the b term in repelling with other pedestrians
        /// </summary>
        /// <param name="relDistance"></param>
        /// <param name="relVelocity"></param>
        /// <returns></returns>
        private static float CalculateSocialB(Vector3 relDistance, Vector3 relVelocity)
        {
            var normRelDistance = relDistance.magnitude;
            var normRelVel = relVelocity.magnitude;
            var t1 = normRelDistance + (relDistance - relVelocity * deltaT_social).magnitude;
            var t2 = deltaT_social * normRelVel;
            return 0.5f * Mathf.Sqrt(t1 * t1 + t2 * t2);
        }

        public static Vector3 RepulsionFromBorder(Vector3 position, Vector3 border)
        {
            var direction = position - border;

            var force = force_obstacle * Mathf.Exp(-1 * (
            direction / sigma_obstacle).magnitude) * direction.normalized;

            return force;
        }

        public static void SetupConfigBinding(BepInEx.Configuration.ConfigFile cfg)
        {
        }
    }

    public struct Forces
    {
        public Vector3 destination, repulsion, attraction, border, fluctuation;

        public Vector3 Sum()
        {
            return destination
                + repulsion
                + attraction
                + border
                + fluctuation;
        }

        public override string ToString()
        {
            return
               $"Forces(dest={destination},\n\trepulsion={repulsion},\n\tattraction={attraction},\n\tborder={border},\n\tfluctuation={fluctuation},\n\tsum={Sum()})"
               ;
        }
    }

    public struct Neighbor<T>
    {
        public T id;
        public Vector3 position;
        public Vector3 velocity;

        public override string ToString()
        {
            return $"Neighbor(id={id}, position={position}, velocity={velocity})";
        }
    }

    public class ActivePerson
    {
        //public Vector3 speed;
    }
}
