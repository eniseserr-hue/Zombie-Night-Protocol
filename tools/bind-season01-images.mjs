import { readFile, writeFile } from "node:fs/promises";

const storyPath = new URL(
  "../content/stories/tr/season-01/chapter-01/scenes.json",
  import.meta.url,
);

const mainImages = {
  s01c01_scene01_wakeup: "s01_scene01_wakeup.webp",
  s01c01_scene02_clinic_corridor: "s01_scene02_clinic_corridor.webp",
  s01c01_scene03_first_infected: "s01_scene03_first_infected.webp",
  s01c01_scene04_hidden_stranger: "s01_scene04_hidden_stranger_hakan.webp",
  s01c01_scene05_exit_together: "s01_scene05_escape_clinic.webp",
  s01c01_scene06_silent_istanbul: "s01_scene06_silent_istanbul.webp",
  s01c01_scene07_first_shelter: "s01_scene07_first_shelter_yesking.webp",
  s01c01_scene08_power_antenna: "s01_scene08_power_antenna.webp",
  s01c01_scene09_derya_recording: "s01_scene09_derya_recording.webp",
  s01c01_scene10_apartment_fall: "s01_scene10_apartment_fall.webp",
  s01c01_scene11_bedo_arrives: "s01_scene11_bedo_arrives.webp",
  s01c01_scene12_first_night_team: "s01_scene12_tailor_workshop_night.webp",
  s01c01_scene13_first_supply_run: "s01_scene13_market_supply_run.webp",
  s01c01_scene14_meet_busra: "s01_scene14_busra_first_meeting.webp",
  s01c01_scene15_chapter_finale: "s01_scene15_chapter_finale.webp",
  s01c01_scene16_ending_narration: "s01_ending_narration.webp",
};

const choiceImages = {
  s01c01_choice_search_quietly: "s01_scene01_choice_search_cabinets.webp",
  s01c01_choice_listen_corridor: "s01_scene01_choice_listen_corridor.webp",
  s01c01_choice_window: "s01_scene01_choice_look_window.webp",
  s01c01_choice_save_battery: "s01_scene01_choice_save_phone.webp",
  s01c01_choice_force_fire_door: "s01_scene02_choice_check_emergency_exit.webp",
  s01c01_choice_medical_supplies: "s01_scene02_choice_go_medicine_storage.webp",
  s01c01_choice_retreat_silent: "s01_scene03_choice_retreat_quietly.webp",
  s01c01_choice_throw_tray: "s01_scene03_choice_throw_metal_tray.webp",
  s01c01_choice_attack_infected: "s01_scene03_choice_attack_with_item.webp",
  s01c01_choice_block_door: "s01_scene03_choice_block_door.webp",
  s01c01_choice_calm_hakan: "s01_scene04_choice_calm_hakan.webp",
  s01c01_choice_treat_hakan: "s01_scene04_choice_treat_hakan_wound.webp",
  s01c01_choice_call_bluff: "s01_scene04_choice_call_hakan_bluff.webp",
  s01c01_choice_leave_hakan_room: "s01_scene04_choice_leave_hakan.webp",
  s01c01_choice_move_silent_street: "s01_scene06_choice_back_alleys.webp",
  s01c01_choice_tell_truth_yesking: "s01_scene07_choice_tell_truth.webp",
  s01c01_choice_ask_derya_recording: "s01_scene07_choice_ask_derya.webp",
  s01c01_choice_protect_yesking: "s01_scene08_choice_protect_yesking.webp",
  s01c01_choice_manual_fuse: "s01_scene08_choice_open_fuse_box.webp",
  s01c01_choice_lure_to_flat: "s01_scene08_choice_distract_infected.webp",
  s01c01_choice_replay_signal: "s01_scene09_choice_replay_recording.webp",
  s01c01_choice_rooftop_escape: "s01_scene10_choice_rooftop_escape.webp",
  s01c01_choice_service_exit: "s01_scene10_choice_service_exit.webp",
  s01c01_choice_move_noise_source: "s01_scene10_choice_move_noise_source.webp",
  s01c01_choice_go_back_hakan: "s01_scene10_choice_save_hakan.webp",
  s01c01_choice_rest_here: "s01_scene12_choice_stay_night.webp",
  s01c01_choice_follow_derya_now: "s01_scene12_choice_follow_derya_now.webp",
  s01c01_choice_find_food_water: "s01_scene12_choice_find_food_water.webp",
  s01c01_choice_take_yesking_market: "s01_scene13_choice_go_with_yesking.webp",
  s01c01_choice_take_bedo_market: "s01_scene13_choice_go_with_bedo.webp",
  s01c01_choice_take_hakan_market: "s01_scene13_choice_go_with_hakan.webp",
  s01c01_choice_leave_medicine_busra: "s01_scene14_choice_leave_medicine.webp",
  s01c01_choice_share_medicine: "s01_scene14_choice_share_medicine.webp",
  s01c01_choice_prioritize_team: "s01_scene14_choice_prioritize_team.webp",
  s01c01_choice_force_medicine: "s01_scene14_choice_force_take_medicine.webp",
  s01c01_choice_go_otogar: "s01_scene15_choice_search_derya_at_otogar.webp",
  s01c01_choice_secure_busra_wounded: "s01_scene15_choice_help_busra_wounded.webp",
  s01c01_choice_search_vehicles: "s01_scene15_choice_search_vehicles.webp",
  s01c01_choice_hide_until_morning: "s01_scene15_choice_hide_until_morning.webp",
};

const document = JSON.parse(await readFile(storyPath, "utf8"));
let boundScenes = 0;
let boundChoices = 0;

for (const scene of document.scenes) {
  const mainImage = mainImages[scene.id];
  if (!mainImage) {
    throw new Error(`No main image mapping for ${scene.id}`);
  }

  scene.sceneImage = `images/story/season01/main/${mainImage}`;
  boundScenes += 1;

  for (const choice of scene.choices ?? []) {
    const choiceImage = choiceImages[choice.id];
    if (!choiceImage) {
      delete choice.choicePreviewImage;
      continue;
    }

    choice.choicePreviewImage =
      `images/story/season01/choices/${choiceImage}`;
    boundChoices += 1;
  }
}

await writeFile(storyPath, `${JSON.stringify(document, null, 2)}\n`, "utf8");
console.log(`Bound ${boundScenes} scenes and ${boundChoices} choices.`);
