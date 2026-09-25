package com.aiusagemonitor.android;

import android.app.Activity;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Bundle;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.TextView;

public final class SettingsActivity extends Activity {
    private static final int BACKGROUND = Color.rgb(12, 19, 29);
    private static final int TEXT = Color.rgb(238, 245, 250);
    private static final int MUTED = Color.rgb(170, 187, 200);
    private static final int ACCENT = Color.rgb(100, 226, 204);
    private static final int[] LABELS = {
        R.string.localized_080, R.string.localized_081, R.string.localized_082, R.string.localized_083,
        R.string.localized_084, R.string.localized_085, R.string.localized_086
    };

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(BACKGROUND);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            view.setPadding(0, insets.getSystemWindowInsetTop(), 0, insets.getSystemWindowInsetBottom());
            return insets;
        });
        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(20), dp(24), dp(20), dp(24));
        scroll.addView(content);
        setContentView(scroll);

        Button back = new Button(this);
        back.setText(getString(R.string.localized_073));
        back.setAllCaps(false);
        back.setOnClickListener(view -> finish());
        content.addView(back, new LinearLayout.LayoutParams(-2, dp(48)));

        TextView title = text(getString(R.string.localized_007), 25, TEXT);
        title.setTypeface(null, Typeface.BOLD);
        content.addView(title);
        TextView heading = text(getString(R.string.localized_074), 18, TEXT);
        heading.setPadding(0, dp(24), 0, dp(8));
        content.addView(heading);
        content.addView(text(getString(R.string.localized_075), 14, MUTED));

        RadioGroup options = new RadioGroup(this);
        options.setOrientation(LinearLayout.VERTICAL);
        content.addView(options);
        int current = WidgetSettings.read(this);
        int[] selectedId = {0};
        for (int i = 0; i < WidgetSettings.INTERVALS.length; i++) {
            RadioButton option = new RadioButton(this);
            option.setId(android.view.View.generateViewId());
            option.setTag(WidgetSettings.INTERVALS[i]);
            option.setText(LABELS[i]);
            option.setTextSize(16);
            option.setTextColor(TEXT);
            option.setButtonTintList(ColorStateList.valueOf(ACCENT));
            option.setMinHeight(dp(48));
            options.addView(option);
            if (WidgetSettings.INTERVALS[i] == current) selectedId[0] = option.getId();
        }
        options.check(selectedId[0]);

        TextView result = text("", 13, MUTED);
        result.setPadding(0, dp(12), 0, dp(12));
        options.setOnCheckedChangeListener((group, checkedId) -> {
            if (checkedId == selectedId[0]) return;
            int minutes = (int) group.findViewById(checkedId).getTag();
            if (!WidgetSettings.save(this, minutes)) {
                group.check(selectedId[0]);
                result.setText(getString(R.string.localized_076));
                return;
            }
            selectedId[0] = checkedId;
            result.setText(WidgetRefreshJob.schedulePeriodic(this)
                ? getString(R.string.localized_077) : getString(R.string.localized_078));
        });
        content.addView(result);
        content.addView(text(getString(R.string.localized_079), 14, MUTED));
    }

    private TextView text(String value, int size, int color) {
        TextView text = new TextView(this);
        text.setText(value);
        text.setTextSize(size);
        text.setTextColor(color);
        return text;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
}
