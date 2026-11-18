namespace OperatorKillhouse.Core;

public sealed record TargetPreview(Target Target,TargetRef? Previous,string? Rejection) {
    public bool CanPlace=>Rejection==null;
}

// Repaint/layout events and motion within one snapped pose share one check.
public sealed class TargetPreviewCache {
    private LayoutDraft? draft;
    private int revision=-1,context;
    private bool strict;
    private TargetPreview? result;
    public TargetPreview Get(LayoutDraft document,Target target,TargetRef? previous,int validationContext) {
        if(result==null || !ReferenceEquals(draft,document) || revision!=document.Revision || strict!=document.StrictValidation || context!=validationContext || result.Target!=target || result.Previous!=previous) {
            result=document.PreviewTarget(previous,target);draft=document;revision=document.Revision;strict=document.StrictValidation;context=validationContext;
        }
        return result;
    }
    public void Clear(){draft=null;result=null;revision=-1;}
}
