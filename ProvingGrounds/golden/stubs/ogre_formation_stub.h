// Minimal Ogre 1.9 stand-ins for golden/gen_formation.cpp (ArenaIngress.cpp's formation maths). KenshiLib's ogre/OgreVector3.h and
// ogre/OgreQuaternion.h cannot be used here, so this file holds ONLY the operations those copied lines call, each body copied from
// OGRE v1-9-0 (github.com/OGRECave/ogre, tag v1-9-0; MIT licence, Copyright (c) 2000-2013 Torus Knot Software Ltd):
//   Real = float (OGRE_DOUBLE_PRECISION off, as Kenshi ships)
//   Radian(Real), Radian::valueRadians, Real * Radian                     OgreMath.h:52, 59, 747-750
//   Math::Sin / Math::Cos (Radian, useTables false: Real(sin(r)))        OgreMath.h:319-321, 381-383
//     Ogre calls the unqualified sin/cos on a float; MSVC's <cmath> (the game's CRT) resolves that to the float overload (sinf/cosf),
//     so the stub calls std::sin / std::cos on the float (the float overloads; C# MathF.Sin / MathF.Cos). Under g++ an unqualified
//     ::sin(float) may pick the C double sin instead, which is why the stub does not copy Ogre's spelling here.
//   Vector3(x,y,z), + - (Vector3), * (Real), *= (Real), /= (Real), crossProduct, UNIT_Y
//                                                                        OgreVector3.h:163-185, 309-315, 326-336, 492-498
//   Quaternion(Radian, Vector3) -> FromAngleAxis                         OgreQuaternion.h:78-81, OgreQuaternion.cpp:116-130
//   Quaternion * Vector3 ("nVidia SDK implementation")                  OgreQuaternion.cpp:399-411
// Ogre::Vector3 operator/= multiplies by fInv = 1.0f / fScalar (it does not divide each component): copied as Ogre has it.
#pragma once
#include <cmath>

namespace Ogre
{
    typedef float Real;

    class Radian
    {
        Real mRad;
    public:
        explicit Radian(Real r = 0) : mRad(r) {}
        Real valueRadians() const { return mRad; }
        Radian operator*(Real f) const { return Radian(mRad * f); }
    };
    inline Radian operator*(Real a, const Radian& b) { return Radian(a * b.valueRadians()); }

    class Math
    {
    public:
        static inline Real Cos(const Radian& fValue, bool useTables = false) { (void)useTables; return Real(std::cos(fValue.valueRadians())); }
        static inline Real Sin(const Radian& fValue, bool useTables = false) { (void)useTables; return Real(std::sin(fValue.valueRadians())); }
    };

    class Vector3
    {
    public:
        Real x, y, z;
        Vector3() {}
        Vector3(const Real fX, const Real fY, const Real fZ) : x(fX), y(fY), z(fZ) {}

        Vector3 operator+(const Vector3& rkVector) const { return Vector3(x + rkVector.x, y + rkVector.y, z + rkVector.z); }
        Vector3 operator-(const Vector3& rkVector) const { return Vector3(x - rkVector.x, y - rkVector.y, z - rkVector.z); }
        Vector3 operator*(const Real fScalar) const { return Vector3(x * fScalar, y * fScalar, z * fScalar); }
        Vector3& operator*=(const Real fScalar) { x *= fScalar; y *= fScalar; z *= fScalar; return *this; }
        Vector3& operator/=(const Real fScalar)
        {
            Real fInv = 1.0f / fScalar;
            x *= fInv;
            y *= fInv;
            z *= fInv;
            return *this;
        }
        Vector3 crossProduct(const Vector3& rkVector) const
        {
            return Vector3(
                y * rkVector.z - z * rkVector.y,
                z * rkVector.x - x * rkVector.z,
                x * rkVector.y - y * rkVector.x);
        }
        static const Vector3 UNIT_Y;
    };
    inline const Vector3 Vector3::UNIT_Y(0.0f, 1.0f, 0.0f);

    class Quaternion
    {
    public:
        Real w, x, y, z;
        Quaternion(Real fW = 1.0f, Real fX = 0.0f, Real fY = 0.0f, Real fZ = 0.0f) : w(fW), x(fX), y(fY), z(fZ) {}
        Quaternion(const Radian& rfAngle, const Vector3& rkAxis) { this->FromAngleAxis(rfAngle, rkAxis); }

        void FromAngleAxis(const Radian& rfAngle, const Vector3& rkAxis)
        {
            Radian fHalfAngle(0.5 * rfAngle);
            Real fSin = Math::Sin(fHalfAngle);
            w = Math::Cos(fHalfAngle);
            x = fSin * rkAxis.x;
            y = fSin * rkAxis.y;
            z = fSin * rkAxis.z;
        }

        Vector3 operator*(const Vector3& v) const
        {
            // nVidia SDK implementation
            Vector3 uv, uuv;
            Vector3 qvec(x, y, z);
            uv = qvec.crossProduct(v);
            uuv = qvec.crossProduct(uv);
            uv *= (2.0f * w);
            uuv *= 2.0f;

            return v + uv + uuv;
        }
    };
}
