// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public sealed record GraphModel(
    string Subset,
    string Configuration,
    NodeModel[] Nodes,
    OutputCollision[] OutputCollisions);
