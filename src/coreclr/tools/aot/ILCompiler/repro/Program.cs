// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

class Program
{
    static void Main() => Console.WriteLine(new Action(Test<string>).Method);

    static void Test<T>() { }
}