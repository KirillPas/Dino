// Copyright © Magnetic Arcade. All Rights Reserved.

#ifndef FLORA_QUATERNION_INCLUDED
#define FLORA_QUATERNION_INCLUDED

#ifndef PI
#define PI 3.14159265359f
#endif

#ifndef FLT_MIN
#define FLT_MIN  1.175494351e-38 // Minimum normalized positive floating-point number
#endif

static const float4 kQuaternionIdentity = float4(0.0, 0.0, 0.0, 1.0);

float4 QuaternionNormalize(float4 x)
{
    float len = dot(x, x);
    return len > FLT_MIN ? rsqrt(len) * x : kQuaternionIdentity;
}

float4 QuaternionFromMatrix(float3x3 m)
{
    float4 q;
    float t;

    if (m[2][2] < 0.0)
    {
        if (m[0][0] > m[1][1])
        {
            t = 1.0 + m[0][0] - m[1][1] - m[2][2];
            q = float4(t, m[0][1] + m[1][0], m[2][0] + m[0][2], m[1][2] - m[2][1]);
        }
        else
        {
            t = 1.0 - m[0][0] + m[1][1] - m[2][2];
            q = float4(m[0][1] + m[1][0], t, m[1][2] + m[2][1], m[2][0] - m[0][2]);
        }
    }
    else
    {
        if (m[0][0] < -m[1][1])
        {
            t = 1.0 - m[0][0] - m[1][1] + m[2][2];
            q = float4(m[2][0] + m[0][2], m[1][2] + m[2][1], t, m[0][1] - m[1][0]);
        }
        else
        {
            t = 1.0 + m[0][0] + m[1][1] + m[2][2];
            q = float4(m[1][2] - m[2][1], m[2][0] - m[0][2], m[0][1] - m[1][0], t);
        }
    }

    return q * 0.5 * rsqrt(t);
}

float4 QuaternionFromAxisAngle(float3 axis, float angle)
{
    float sina, cosa;
    sincos(0.5 * angle, sina, cosa);
    return float4(axis * sina, cosa);
}

float4 QuaternionRotateX(float angle)
{
    return QuaternionFromAxisAngle(float3(1.0, 0.0, 0.0), angle);
}

float4 QuaternionRotateY(float angle)
{
    return QuaternionFromAxisAngle(float3(0.0, 1.0, 0.0), angle);
}

float4 QuaternionRotateZ(float angle)
{
    return QuaternionFromAxisAngle(float3(0.0, 0.0, 1.0), angle);
}

float4 QuaternionLookRotation(float3 forward, float3 up)
{
    float3 t = normalize(cross(up, forward));
    return QuaternionFromMatrix(float3x3(t, cross(forward, t), forward));
}

// https://stackoverflow.com/questions/1171849/finding-quaternion-representing-the-rotation-from-one-vector-to-another
float4 QuaternionFromToRotation(float3 a, float3 b)
{
    float4 q;
    float d = dot(a, b);
    if (d < -0.999999)
    {
        float3 r = float3(1.0, 0.0, 0.0);
        float3 u = float3(0.0, 1.0, 0.0);
        float3 t = cross(r, a);
        if (length(t) < FLT_MIN)
            t = cross(u, a);

        t = normalize(t);
        q = QuaternionFromAxisAngle(t, PI);
    }
    else if (d > 0.999999)
    {
        q = kQuaternionIdentity;
    }
    else
    {
        q.xyz = cross(a, b);
        q.w = 1.0 + d;
        q = normalize(q);
    }
    return q;
}

float4 QuaternionMultiply(float4 q1, float4 q2)
{
    return float4(
        q2.xyz * q1.w + q1.xyz * q2.w + cross(q1.xyz, q2.xyz),
        q1.w * q2.w - dot(q1.xyz, q2.xyz)
    );
}

float3 QuaternionRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

float3x3 QuaternionToMatrix(float4 q)
{
    float3x3 m;

    float xx = q.x * q.x;
    float yy = q.y * q.y;
    float zz = q.z * q.z;
    float xy = q.x * q.y;
    float xz = q.x * q.z;
    float yz = q.y * q.z;
    float wx = q.w * q.x;
    float wy = q.w * q.y;
    float wz = q.w * q.z;

    m[0][0] = 1.0 - 2.0 * (yy + zz);
    m[0][1] = 2.0 * (xy - wz);
    m[0][2] = 2.0 * (xz + wy);

    m[1][0] = 2.0 * (xy + wz);
    m[1][1] = 1.0 - 2.0 * (xx + zz);
    m[1][2] = 2.0 * (yz - wx);

    m[2][0] = 2.0 * (xz - wy);
    m[2][1] = 2.0 * (yz + wx);
    m[2][2] = 1.0 - 2.0 * (xx + yy);

    return m;
}

#endif // FLORA_QUATERNION_INCLUDED
