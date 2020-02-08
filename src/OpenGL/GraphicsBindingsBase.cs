//
// The Open Toolkit Library License
//
// Copyright (c) 2006 - 2009 the Open Toolkit library.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Diagnostics;

namespace OpenToolkit.Graphics
{
    /// <summary>
    /// Implements BindingsBase for the OpenTK.Graphics namespace (OpenGL and OpenGL|ES).
    /// </summary>
    public abstract class GraphicsBindingsBase : BindingsBase
    {
        internal IntPtr[] _EntryPointsInstance;
        internal string[] _EntryPointNamesInstance;

        /// <summary>
        /// Loads all the available bindings for the current context.
        /// </summary>
        /// <param name="context">The context used to query the available bindings.</param>
        /// <remarks>
        /// Loads all available entry points for the current OpenGL context.
        /// </remarks>
        public override void LoadBindings(IBindingsContext context)
        {
            Debug.Print("Loading entry points for {0}", GetType().FullName);

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            for (int i = 0; i < _EntryPointsInstance.Length; i++)
            {
                _EntryPointsInstance[i] = context.GetAddress(_EntryPointNamesInstance[i]);
            }
        }
    }
}
