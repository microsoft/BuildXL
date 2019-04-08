// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// CompletionsRequest request; value of the <see cref="IRequest.Command"/> field is 'completions'.
    ///
    /// Returns a list of possible completions for a given caret position and text. The <see cref="ICompletionsCommand"/>
    /// may only be called if the <see cref="ICapabilities.SupportsCompletionsRequest"/> capability exists and is true.
    /// </summary>
    public interface ICompletionsCommand : ICommand<ICompletionsResult>
    {
        /// <summary>Returns completions in the scope of this stack frame. If not specified, the completions are returned for the global scope.</summary>
        int? FrameId { get; }

        /// <summary>One or more source lines. Typically this is the text a user has typed into the debug console before he asked for completion.</summary>
        string Text { get; }

        /// <summary>The character position for which to determine the completion proposals.</summary>
        int Column { get; }

        /// <summary>An optional line for which to determine the completion proposals. If missing the first line of the text is assumed.</summary>
        int? Line { get; }
    }

    /// <summary>
    /// Response to <see cref="ICompletionsCommand"/>.
    /// </summary>
    public interface ICompletionsResult
    {
        /// <summary>The possible stepIn targets of the specified source location.</summary>
        IReadOnlyList<ICompletionItem> Targets { get; }
    }

    /// <summary>
    /// <see cref="ICompletionItem"/>s are the suggestions returned from the <see cref="ICompletionsCommand"/>.
    /// </summary>
    public interface ICompletionItem
    {
        /// <summary>The label of this completion item. By default this is also the text that is inserted when selecting this completion.</summary>
        string Label { get; }

        /// <summary>If text is not null or empty then it is inserted instead of the label.</summary>
        string Text { get; }

        /// <summary>The item's type. Typically the client uses this information to render the item in the UI with an icon.</summary>
        CompletionItemType Type { get; }

        /// <summary>When a completion is selected it replaces 'length' characters starting at 'start' in the text passed to the <see cref="ICompletionsCommand"/>.</summary>
        int? Start { get; }

        /// <summary>When a completion is selected it replaces 'length' characters starting at 'start' in the text passed to the <see cref="ICompletionsCommand"/>.</summary>
        int? Length { get; }
    }

#pragma warning disable SA1300 // Element must begin with an uppercase letter

    /// <summary>
    /// Some predefined types for the CompletionItem. Please note that not all clients have specific icons for all of them.
    /// </summary>
    public enum CompletionItemType
    {
        /// <nodoc/>
        method,

        /// <nodoc/>
        function,

        /// <nodoc/>
        constructor,

        /// <nodoc/>
        field,

        /// <nodoc/>
        variable,

        /// <nodoc/>
        @class,

        /// <nodoc/>
        @interface,

        /// <nodoc/>
        module,

        /// <nodoc/>
        property,

        /// <nodoc/>
        unit,

        /// <nodoc/>
        value,

        /// <nodoc/>
        @enum,

        /// <nodoc/>
        keyword,

        /// <nodoc/>
        snippet,

        /// <nodoc/>
        text,

        /// <nodoc/>
        color,

        /// <nodoc/>
        file,

        /// <nodoc/>
        reference,

        /// <nodoc/>
        customcolor,
    }
}
