using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Sonosthesia.Signals.Editor
{
    [CustomEditor(typeof(ControlSignalTarget))]
    public sealed class ControlSignalTargetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            var runtime = new Foldout
            {
                text = "Runtime Signal State",
                value = true
            };
            var status = new Label();
            var evaluations = new Label();
            var routes = new Label();
            runtime.Add(status);
            runtime.Add(evaluations);
            runtime.Add(routes);
            root.Add(runtime);

            root.Add(new Button(() => ((ControlSignalTarget)target).ResetEvaluationState())
            {
                text = "Reset Evaluation State"
            });

            root.schedule.Execute(() =>
            {
                var controlTarget = target as ControlSignalTarget;
                if (controlTarget == null)
                    return;
                status.text = controlTarget.HasFrame ? "Frame: available" : "Frame: unavailable";
                evaluations.text = $"Evaluations: {controlTarget.EvaluationCount}";
                routes.text = $"Resolved routes: {controlTarget.ResolvedRouteCount}";
            }).Every(100);
            return root;
        }
    }
}
