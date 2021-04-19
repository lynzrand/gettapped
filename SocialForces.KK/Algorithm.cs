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
        private const float relaxationTime = 0.5f;

        // delta_t
        private const float velocityPredictionTime = 2f;

        // sigma
        private const float velocityRepulsiveRange = 0.3f;

        // V_{\alpha\beta}^0
        private const float velocityRepulsiveCoeff = 2.1f;

        private const float effectiveAngle = 100f;
        private const float effectiveCoeff = 0.5f;

        private const float fluctuationCoeff = 0.02f;

        public static Forces CalculateAcceleration(
            int currentIdentifier,
            Vector3 position,
            Vector3 desiredDirection,
            Vector3 currentSpeed,
            float desiredSpeed,
            IEnumerable<Pedestrian> otherPedestrians,
            Func<int, int, float> repulsionCoeff,
            Vector3? border,
            IEnumerable<Pedestrian> attractions,
            Func<int, int, float> attractionCoeff,
            bool debug = false
        )
        {
            desiredDirection.y = 0;
            desiredDirection.Normalize();
            // Destination term
            var destinationTerm = (1 / relaxationTime) * (desiredSpeed * desiredDirection - currentSpeed);

            // Other pedestrian repulsion term
            var repulsionTerm = Vector3.zero;
            foreach (var repulsor in otherPedestrians)
            {
                var repulsion = RepulsionFromPedestrian(position, repulsor, repulsionCoeff(currentIdentifier, repulsor.id));

                if (Vector3.Angle((Vector3)desiredDirection, repulsor.position - position) > effectiveAngle)
                    repulsion *= effectiveCoeff;

                repulsionTerm += repulsion;
            }

            // wall term
            var borderRepulsionTerm = Vector3.zero;
            if (border != null)
            {
                borderRepulsionTerm = -RepulsionFromBorder(position, border.Value);
                if (Vector3.Angle((Vector3)desiredDirection, border.Value - position) > effectiveAngle)
                    borderRepulsionTerm *= effectiveCoeff;
            }

            // attraction term
            var attractionTerm = Vector3.zero;
            foreach (var attractor in attractions)
            {
                var attraction = RepulsionFromPedestrian(position, attractor, attractionCoeff(currentIdentifier, attractor.id));

                if (Vector3.Angle((Vector3)desiredDirection, attractor.position - position) > effectiveAngle)
                    attraction *= effectiveCoeff;

                attractionTerm += attraction;
            }

            var fluctuations = UnityEngine.Random.insideUnitCircle * fluctuationCoeff;

            if (debug)
            {
                Plugin.Instance.Logger.LogInfo(
                    $"Forces(id={currentIdentifier},\ncurrentPos={position}, destPos={desiredDirection},\ndestinationTerm={destinationTerm}, repulsion={repulsionTerm}, attraction={attractionTerm}, border={borderRepulsionTerm}, fluctuation={fluctuations})"
                    );
            }

            var fluctuations3 = new Vector3(fluctuations.x, 0, fluctuations.y);
            return new Forces()
            {
                destination = destinationTerm,
                attraction = attractionTerm,
                repulsion = repulsionTerm,
                border = borderRepulsionTerm,
                fluctuation = fluctuations3
            };
        }

        public static Vector3 RepulsionFromPedestrian(Vector3 position, Pedestrian pedestrian, float coeff)
        {
            var direction = pedestrian.position - position;
            var dir_mag = direction.magnitude;

            var vb_t = velocityPredictionTime * pedestrian.velocity;
            var vbt_mag2 = vb_t.sqrMagnitude;

            var dir_vbt = direction - vb_t;
            var dir_vbt_mag = dir_vbt.magnitude;

            float dir_mag_sq = (dir_mag + dir_vbt_mag) * (dir_mag + dir_vbt_mag);
            float power = Mathf.Sqrt(-vbt_mag2 + dir_mag_sq);

            float common_term =
                Mathf.Exp(power) * velocityRepulsiveCoeff
                * (dir_mag + dir_vbt_mag)
                / (velocityRepulsiveRange * power);

            var grad_x = common_term * (direction.x / dir_mag + dir_vbt.x / dir_vbt_mag);
            var grad_z = common_term * (direction.z / dir_mag + dir_vbt.z / dir_vbt_mag);

            return new Vector3(grad_x, 0, grad_z);
        }

        public static Vector3 RepulsionFromBorder(Vector3 position, Vector3 border)
        {
            var direction = border - position;
            var mag = direction.magnitude / 0.2f;

            float expmag = Mathf.Exp(-mag);
            var grad_x = expmag * direction.x / mag;
            var grad_z = expmag * direction.z / mag;

            return new Vector3(grad_x, 0, grad_z);
        }
    }

    public struct Forces
    {
        public Vector3 destination, repulsion, attraction, border, fluctuation;

        public Vector3 Sum()
        {
            return destination
                //+ repulsion
                + attraction
                + border
                + fluctuation;
        }

        public override string ToString()
        {
            return
               $"Forces(dest={destination}, repulsion={repulsion}, attraction={attraction}, border={border}, fluctuation={fluctuation})"
               ;
        }
    }

    public struct Pedestrian
    {
        public int id;
        public Vector3 position;
        public Vector3 velocity;
    }

    public class ActivePerson
    {
        //public Vector3 speed;
    }
}
