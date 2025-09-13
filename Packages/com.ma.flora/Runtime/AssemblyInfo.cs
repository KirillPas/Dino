// Copyright © Magnetic Arcade. All Rights Reserved.

using System.Runtime.CompilerServices;
using Unity.Burst;

[assembly: InternalsVisibleTo("MA.Flora.Editor")]
[assembly: BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
