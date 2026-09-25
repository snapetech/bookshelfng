#!/bin/bash

source /usr/share/yunohost/helpers

bookshelfng_prepare_service() {
	install -d -o "$app" -g "$app" -m 0750 "$data_dir/logs"
	ynh_config_add_nginx
	ynh_config_add_systemd
}

bookshelfng_start_service() {
	ynh_systemctl --service="$app" --action="start" \
		--line_match="HTTP server: bindAddress=127.0.0.1" \
		--log_path="$data_dir/logs/readarr.txt"
}

bookshelfng_register_service() {
	local log_file="$data_dir/logs/readarr.txt"

	if [[ ! -f "$log_file" ]]; then
		ynh_die --message="BookshelfNG did not create its application log during startup."
	fi
	yunohost service add "$app" \
		--description="BookshelfNG ebook and audiobook library manager" \
		--log="$log_file"
}
